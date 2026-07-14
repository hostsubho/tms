using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Portal;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 7 - Customer/End-User Portal. Every action here is additionally
// scoped to CustomerId == the caller's own id, on top of the DbContext's
// TenantId query filter - a customer must only ever see their own tickets,
// never another customer's or the tenant's full queue (that's what
// /api/tickets, the staff surface, is for).
[ApiController]
[Route("api/portal/tickets")]
[Authorize(Policy = "PortalCustomer")]
public class PortalTicketsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications;
    private readonly IRuleEngineService _ruleEngine;
    private readonly IAuditLogService _auditLog;
    private readonly IWebhookService _webhooks;

    public PortalTicketsController(TmsDbContext db, ITenantContext tenantContext, INotificationService notifications, IRuleEngineService ruleEngine, IAuditLogService auditLog, IWebhookService webhooks)
    {
        _db = db;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _ruleEngine = ruleEngine;
        _auditLog = auditLog;
        _webhooks = webhooks;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PortalTicketResponse>>> GetMyTickets(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        var customerId = User.GetCustomerId();

        var tickets = await _db.Tickets
            .Where(t => t.CustomerId == customerId)
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

        return Ok(tickets.Select(PortalTicketResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortalTicketResponse>> GetMyTicket(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        var customerId = User.GetCustomerId();
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);
        if (ticket is null) return NotFound();

        var utcNow = DateTime.UtcNow;
        if (await CheckAndNotifyBreachAsync(ticket, tenantId, utcNow, ct)) await _db.SaveChangesAsync(ct);

        return Ok(PortalTicketResponse.FromEntity(ticket));
    }

    // Module 8 - Notifications: same wrapper as TicketsController's, kept as
    // a separate copy rather than a shared static helper since it depends on
    // the injected INotificationService instance.
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
    public async Task<ActionResult<PortalTicketResponse>> CreateTicket([FromBody] PortalCreateTicketRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        var customerId = User.GetCustomerId();

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Subject = request.Subject,
            Description = request.Description,
            Priority = request.Priority,
            Status = TicketStatus.New,
            CustomerId = customerId,
            CreatedAt = DateTime.UtcNow,
        };

        // Same server-side SLA matching as the staff-facing CreateTicket -
        // a customer can't pick a category/assignee or influence which SLA
        // policy applies any more than a staff-created ticket can.
        var policies = await _db.SlaPolicies.ToListAsync(ct);
        var matchedPolicy = SlaEvaluator.FindMatchingPolicy(policies, ticket.Priority);
        SlaEvaluator.ApplyPolicyToNewTicket(ticket, matchedPolicy);

        _db.Tickets.Add(ticket);

        // Actor is the customer, not a staff AppUser - GetUserId() would
        // throw here (portal tokens carry "sub" as the customer's own id,
        // not a NameIdentifier claim staff tokens use - see GetCustomerId's
        // comment), so actorUserId is left null and the label makes clear
        // this came from the portal, not internal staff.
        _auditLog.Record(tenantId, actorUserId: null, $"Customer: {User.GetEmail()}", AuditAction.Created,
            AuditEntityType.Ticket, ticket.Id, $"Created ticket '{ticket.Subject}' via customer portal.");

        // Module 5 - Workflow Automation: a portal-submitted ticket can be
        // auto-assigned/reprioritized by a TicketCreated rule exactly like a
        // staff-created one - intake channel shouldn't change which rules
        // apply.
        await _ruleEngine.RunTriggerAsync(tenantId, AutomationTrigger.TicketCreated, ticket, ct);

        // Module 8 - Notifications: a customer-submitted ticket has no
        // assignee unless an automation rule above just gave it one - notify
        // that assignee directly if so, otherwise every Admin gets notified
        // to triage it (the previous, pre-automation default).
        if (ticket.AssigneeId is not null)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.TicketAssigned,
                $"You were assigned '{ticket.Subject}'.", ticket.Id, ct);
        }
        else
        {
            await _notifications.NotifyAdminsAsync(tenantId, NotificationType.NewTicket,
                $"New ticket from the customer portal: '{ticket.Subject}'.", ticket.Id, ct);
        }

        // Module 11 - Integrations & Public API: same TicketCreated webhook
        // as the staff and public-API intake paths - a subscriber shouldn't
        // miss portal-submitted tickets.
        await _webhooks.NotifyTicketCreatedAsync(tenantId, ticket, ct);

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetMyTicket), new { id = ticket.Id }, PortalTicketResponse.FromEntity(ticket));
    }

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IEnumerable<PortalCommentResponse>>> GetComments(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var ownsTicket = await _db.Tickets.AnyAsync(t => t.Id == id && t.CustomerId == customerId, ct);
        if (!ownsTicket) return NotFound();

        // Internal agent notes are never visible through the portal, however
        // this endpoint is reached - filtered here, not just in the UI.
        var comments = await _db.TicketComments
            .Where(c => c.TicketId == id && !c.IsInternal)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(comments.Select(PortalCommentResponse.FromEntity));
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<PortalCommentResponse>> AddComment(Guid id, [FromBody] PortalAddCommentRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        var customerId = User.GetCustomerId();

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);
        if (ticket is null) return NotFound();

        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TicketId = id,
            AuthorId = customerId,
            Body = request.Body,
            IsInternal = false, // a customer can never post an internal note
            IsFromCustomer = true,
            CreatedAt = DateTime.UtcNow,
        };

        _db.TicketComments.Add(comment);

        // Deliberately does NOT set ticket.FirstRespondedAt - the response
        // SLA measures how fast staff reply to the customer, not the
        // customer's own messages. Only TicketsController.AddComment (the
        // staff surface) advances it.

        // Module 5 - Workflow Automation: e.g. a rule that auto-assigns a
        // ticket the moment its customer replies, if it's still sitting
        // unassigned. Runs before the notify-assignee check below, so a
        // rule-driven assignment here also gets told about the reply that
        // triggered it.
        await _ruleEngine.RunTriggerAsync(tenantId, AutomationTrigger.CustomerReplyReceived, ticket, ct);

        // Module 8 - Notifications: tell the assignee a customer replied.
        // If nobody's assigned yet (still, even after automation above),
        // this reply just sits in the queue same as it always did - there's
        // no "notify all agents" fallback here, unlike NewTicket/SlaBreach,
        // since a reply on an unassigned ticket isn't a new event for the
        // whole team the way a fresh ticket is.
        if (ticket.AssigneeId is not null)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.NewComment,
                $"New customer reply on '{ticket.Subject}'.", ticket.Id, ct);
        }

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetComments), new { id }, PortalCommentResponse.FromEntity(comment));
    }

    [HttpPost("{id:guid}/csat")]
    public async Task<ActionResult<PortalTicketResponse>> SubmitCsat(Guid id, [FromBody] PortalCsatRequest request, CancellationToken ct)
    {
        if (request.Rating is < 1 or > 5)
        {
            return BadRequest(new { message = "Rating must be between 1 and 5." });
        }

        var customerId = User.GetCustomerId();
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);
        if (ticket is null) return NotFound();

        if (ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            return BadRequest(new { message = "This ticket hasn't been resolved yet." });
        }

        if (ticket.CsatSubmittedAt is not null)
        {
            return Conflict(new { message = "A rating has already been submitted for this ticket." });
        }

        ticket.CsatRating = request.Rating;
        ticket.CsatSubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(PortalTicketResponse.FromEntity(ticket));
    }
}
