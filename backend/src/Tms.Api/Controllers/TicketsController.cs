using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Assets;
using Tms.Api.Dtos.Tickets;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize(Policy = "TenantStaff")]
public class TicketsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications;
    private readonly IRuleEngineService _ruleEngine;
    private readonly IAuditLogService _auditLog;
    private readonly IWebhookService _webhooks;

    public TicketsController(TmsDbContext db, ITenantContext tenantContext, INotificationService notifications, IRuleEngineService ruleEngine, IAuditLogService auditLog, IWebhookService webhooks)
    {
        _db = db;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _ruleEngine = ruleEngine;
        _auditLog = auditLog;
        _webhooks = webhooks;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TicketResponse>>> GetTickets(
        [FromQuery] TicketStatus? status,
        [FromQuery] Guid? assigneeId,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        // TenantId filter is applied automatically via the DbContext global query filter.
        var query = _db.Tickets.AsQueryable();
        if (status is not null) query = query.Where(t => t.Status == status);
        if (assigneeId is not null) query = query.Where(t => t.AssigneeId == assigneeId);

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var utcNow = DateTime.UtcNow;
        var changed = false;
        foreach (var ticket in tickets)
        {
            if (await CheckAndNotifyBreachAsync(ticket, tenantId, utcNow, ct)) changed = true;
        }
        if (changed) await _db.SaveChangesAsync(ct);

        return Ok(tickets.Select(t => TicketResponse.FromEntity(t, utcNow)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketResponse>> GetTicket(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        var utcNow = DateTime.UtcNow;
        // Lazily evaluated on read rather than via a background job - there's
        // no worker/scheduler in this deployment, so "automatic" escalation
        // happens the next time anyone views the ticket or the list. Good
        // enough for the spec's "shows on the SLA dashboard before and after
        // breach" bar; a real cron-based sweep would catch breaches even for
        // tickets nobody happens to look at.
        if (await CheckAndNotifyBreachAsync(ticket, tenantId, utcNow, ct)) await _db.SaveChangesAsync(ct);

        return Ok(TicketResponse.FromEntity(ticket, utcNow));
    }

    // Module 8 - Notifications: wraps SlaEvaluator.CheckAndEscalate so a
    // breach that just got caught (lazily, on this very read) also notifies
    // someone - the assignee if the ticket has one, otherwise every Admin,
    // same fallback used for a brand new unassigned ticket.
    private async Task<bool> CheckAndNotifyBreachAsync(Ticket ticket, Guid tenantId, DateTime utcNow, CancellationToken ct)
    {
        if (!SlaEvaluator.CheckAndEscalate(ticket, utcNow)) return false;

        var message = $"Ticket '{ticket.Subject}' breached its SLA and was escalated to {ticket.Priority}.";
        if (ticket.AssigneeId is not null)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.SlaBreach, message, ticket.Id, ct);
        }
        else
        {
            await _notifications.NotifyAdminsAsync(tenantId, NotificationType.SlaBreach, message, ticket.Id, ct);
        }

        return true;
    }

    [HttpPost]
    public async Task<ActionResult<TicketResponse>> CreateTicket([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var utcNow = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Subject = request.Subject,
            Description = request.Description,
            Priority = request.Priority,
            Status = TicketStatus.New,
            CategoryId = request.CategoryId,
            AssigneeId = request.AssigneeId,
            RequesterId = User.GetUserId(),
            CreatedAt = utcNow,
        };

        // SlaPolicyId is always derived server-side from the tenant's SLA
        // policies matched against Priority - never trusted from the request
        // body (same pattern as TenantId/RequesterId above), so a caller
        // can't hand-pick a more lenient SLA for their own ticket.
        var policies = await _db.SlaPolicies.ToListAsync(ct);
        var matchedPolicy = SlaEvaluator.FindMatchingPolicy(policies, ticket.Priority);
        SlaEvaluator.ApplyPolicyToNewTicket(ticket, matchedPolicy);

        _db.Tickets.Add(ticket);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.Ticket, ticket.Id, $"Created ticket '{ticket.Subject}'.");

        // Module 5 - Workflow Automation: runs before the notify-on-create
        // block below, so if a rule auto-assigns this ticket (or changes its
        // priority/status), the notification logic sees the *final* state -
        // e.g. a rule-assigned ticket correctly notifies that assignee
        // instead of falling through to "every Admin."
        await _ruleEngine.RunTriggerAsync(tenantId, AutomationTrigger.TicketCreated, ticket, ct);

        // Module 8 - Notifications: a brand new ticket needs someone to
        // triage it. If it was created pre-assigned (by the request or by an
        // automation rule above), tell that assignee directly; otherwise
        // every Admin gets a heads-up.
        if (ticket.AssigneeId is not null)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.TicketAssigned,
                $"You were assigned '{ticket.Subject}'.", ticket.Id, ct);
        }
        else
        {
            await _notifications.NotifyAdminsAsync(tenantId, NotificationType.NewTicket,
                $"New ticket needs triage: '{ticket.Subject}'.", ticket.Id, ct);
        }

        // Module 11 - Integrations & Public API: fires regardless of intake
        // channel, same as Notifications/RuleEngine above - a tenant's
        // TicketCreated webhook subscription should hear about this ticket
        // whether it came from the staff UI, the customer portal, or the
        // public API.
        await _webhooks.NotifyTicketCreatedAsync(tenantId, ticket, ct);

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTicket), new { id = ticket.Id }, TicketResponse.FromEntity(ticket, utcNow));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<TicketResponse>> UpdateTicket(Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        var previousAssigneeId = ticket.AssigneeId;
        var previousStatus = ticket.Status;

        if (request.Subject is not null) ticket.Subject = request.Subject;
        if (request.Description is not null) ticket.Description = request.Description;
        if (request.Status is not null)
        {
            // Stamps/clears ResolvedAt as part of the same transition - see
            // TicketStatusTransition for why this lives in one shared place.
            TicketStatusTransition.ApplyStatus(ticket, request.Status.Value);
        }
        if (request.Priority is not null) ticket.Priority = request.Priority.Value;
        if (request.CategoryId is not null) ticket.CategoryId = request.CategoryId;
        if (request.AssigneeId is not null) ticket.AssigneeId = request.AssigneeId;

        // Built from exactly which fields this request touched, before the
        // automation rule engine runs below - a rule-driven change to the
        // same ticket gets its own separate audit entry (see
        // RuleEngineService), so this one should only describe what the
        // human caller of this PATCH actually asked for.
        var changedFields = new List<string>();
        if (request.Subject is not null) changedFields.Add("subject");
        if (request.Description is not null) changedFields.Add("description");
        if (request.Status is not null) changedFields.Add($"status → {ticket.Status}");
        if (request.Priority is not null) changedFields.Add($"priority → {ticket.Priority}");
        if (request.CategoryId is not null) changedFields.Add("category");
        if (request.AssigneeId is not null) changedFields.Add("assignee");
        if (changedFields.Count > 0)
        {
            _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
                AuditEntityType.Ticket, ticket.Id, $"Updated ticket '{ticket.Subject}': {string.Join(", ", changedFields)}.");
        }

        // Module 5 - Workflow Automation: runs after the request's own
        // field changes are applied (so conditions see the ticket's new
        // state, e.g. a rule matching on the priority this same PATCH just
        // set) and before the reassignment-notify check below, so a
        // rule-driven reassignment is notified exactly like a manual one.
        await _ruleEngine.RunTriggerAsync(tenantId, AutomationTrigger.TicketUpdated, ticket, ct);

        // Module 8 - Notifications: fire when the assignee actually changed
        // to someone new - whether from this PATCH's own AssigneeId or from
        // an automation rule above - not on every PATCH that happens to
        // re-send the same AssigneeId alongside an unrelated status change.
        if (ticket.AssigneeId is not null && ticket.AssigneeId != previousAssigneeId)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.TicketAssigned,
                $"You were assigned '{ticket.Subject}'.", ticket.Id, ct);
        }

        // Module 11 - Integrations & Public API: fires whether this PATCH's
        // own Status field or an automation rule above is what actually
        // changed it - a subscriber cares that the status changed, not which
        // surface triggered it.
        if (ticket.Status != previousStatus)
        {
            await _webhooks.NotifyTicketStatusChangedAsync(tenantId, ticket, previousStatus, ct);
        }

        await _db.SaveChangesAsync(ct);
        var utcNow = DateTime.UtcNow;
        return Ok(TicketResponse.FromEntity(ticket, utcNow));
    }

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IEnumerable<CommentResponse>>> GetComments(Guid id, CancellationToken ct)
    {
        var ticketExists = await _db.Tickets.AnyAsync(t => t.Id == id, ct);
        if (!ticketExists) return NotFound();

        var comments = await _db.TicketComments
            .Where(c => c.TicketId == id)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(comments.Select(CommentResponse.FromEntity));
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentResponse>> AddComment(Guid id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        var utcNow = DateTime.UtcNow;
        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TicketId = id,
            AuthorId = User.GetUserId(),
            Body = request.Body,
            IsInternal = request.IsInternal,
            CreatedAt = utcNow,
        };

        _db.TicketComments.Add(comment);

        // Module 4 - SLA Management: first comment of any kind counts as the
        // "first response" for response-SLA purposes (see SlaEvaluator).
        if (ticket.FirstRespondedAt is null)
        {
            ticket.FirstRespondedAt = utcNow;
        }

        // Module 8 - Notifications: this is the staff surface, so every
        // comment posted here is from an agent - if the ticket has a portal
        // customer attached, let them know a reply is waiting (unless it's
        // an internal note, which they were never going to see anyway).
        if (ticket.CustomerId is not null && !comment.IsInternal)
        {
            await _notifications.NotifyCustomerAsync(tenantId, ticket.CustomerId.Value, NotificationType.NewComment,
                $"You have a new reply on '{ticket.Subject}'.", ticket.Id, ct);
        }

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetComments), new { id }, CommentResponse.FromEntity(comment));
    }

    // Module 10 - Asset Management/CMDB: the reverse of
    // AssetsController.GetAssetTickets, for the ticket detail page's "linked
    // assets" panel. Gated by the same Tenant.CmdbEnabled flag - a tenant
    // without CMDB turned on simply never has any TicketAssets rows to
    // begin with, but this returns 403 rather than a silently-empty list so
    // the frontend can distinguish "no assets linked yet" from "this
    // workspace doesn't have CMDB."
    [HttpGet("{id:guid}/assets")]
    public async Task<ActionResult<IEnumerable<LinkedAssetSummary>>> GetLinkedAssets(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is not { CmdbEnabled: true })
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Asset Management (CMDB) isn't enabled for this workspace yet - contact WMX to turn it on." });
        }

        var ticketExists = await _db.Tickets.AnyAsync(t => t.Id == id, ct);
        if (!ticketExists) return NotFound();

        var assetIds = await _db.TicketAssets.Where(l => l.TicketId == id).Select(l => l.AssetId).ToListAsync(ct);
        var assets = await _db.Assets.Where(a => assetIds.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(ct);

        return Ok(assets.Select(a => new LinkedAssetSummary(a.Id, a.Name, a.Type.ToString(), a.Status.ToString())));
    }
}
