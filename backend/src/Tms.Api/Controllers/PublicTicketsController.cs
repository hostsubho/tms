using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.PublicApi;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 11 - Integrations & Public API. A tenant's own external systems
// authenticate here via an API key (X-Api-Key header, see
// ApiKeyAuthenticationHandler) rather than a staff/portal JWT. Deliberately
// a narrower surface than the staff TicketsController - no Subject/
// Description edits, no comments, no CSAT - scoped to exactly what the
// spec's done-when bar needs: create a ticket via the API, and change its
// status enough to trigger a webhook. Versioned (v1) so the shape can
// evolve later without breaking existing integrations.
[ApiController]
[Route("api/v1/tickets")]
[Authorize(Policy = "PublicApi")]
public class PublicTicketsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IRuleEngineService _ruleEngine;
    private readonly INotificationService _notifications;
    private readonly IAuditLogService _auditLog;
    private readonly IWebhookService _webhooks;

    public PublicTicketsController(
        TmsDbContext db,
        ITenantContext tenantContext,
        IRuleEngineService ruleEngine,
        INotificationService notifications,
        IAuditLogService auditLog,
        IWebhookService webhooks)
    {
        _db = db;
        _tenantContext = tenantContext;
        _ruleEngine = ruleEngine;
        _notifications = notifications;
        _auditLog = auditLog;
        _webhooks = webhooks;
    }

    private string ActorLabel => $"API key: {User.GetApiKeyName()}";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PublicTicketResponse>>> GetTickets(
        [FromQuery] TicketStatus? status, CancellationToken ct)
    {
        var query = _db.Tickets.AsQueryable();
        if (status is not null) query = query.Where(t => t.Status == status);

        var tickets = await query.OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
        return Ok(tickets.Select(PublicTicketResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicTicketResponse>> GetTicket(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();
        return Ok(PublicTicketResponse.FromEntity(ticket));
    }

    [HttpPost]
    public async Task<ActionResult<PublicTicketResponse>> CreateTicket([FromBody] CreatePublicTicketRequest request, CancellationToken ct)
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
            // No AppUser or PortalCustomer to attribute this to - both
            // RequesterId and CustomerId stay null, same as any other
            // ticket with no human intake. The audit log entry below still
            // records which API key created it.
            CreatedAt = utcNow,
        };

        // Same server-side SLA matching every other intake path uses - a
        // caller can't pick a more lenient SLA any more than a staff- or
        // portal-created ticket can.
        var policies = await _db.SlaPolicies.ToListAsync(ct);
        var matchedPolicy = SlaEvaluator.FindMatchingPolicy(policies, ticket.Priority);
        SlaEvaluator.ApplyPolicyToNewTicket(ticket, matchedPolicy);

        _db.Tickets.Add(ticket);

        _auditLog.Record(tenantId, actorUserId: null, ActorLabel, AuditAction.Created,
            AuditEntityType.Ticket, ticket.Id, $"Created ticket '{ticket.Subject}' via public API.");

        await _ruleEngine.RunTriggerAsync(tenantId, AutomationTrigger.TicketCreated, ticket, ct);

        if (ticket.AssigneeId is not null)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.TicketAssigned,
                $"You were assigned '{ticket.Subject}'.", ticket.Id, ct);
        }
        else
        {
            await _notifications.NotifyAdminsAsync(tenantId, NotificationType.NewTicket,
                $"New ticket from the public API: '{ticket.Subject}'.", ticket.Id, ct);
        }

        await _webhooks.NotifyTicketCreatedAsync(tenantId, ticket, ct);

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTicket), new { id = ticket.Id }, PublicTicketResponse.FromEntity(ticket));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PublicTicketResponse>> UpdateTicket(Guid id, [FromBody] UpdatePublicTicketRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        var previousStatus = ticket.Status;
        var previousAssigneeId = ticket.AssigneeId;

        if (request.Status is not null)
        {
            TicketStatusTransition.ApplyStatus(ticket, request.Status.Value);
        }
        if (request.Priority is not null) ticket.Priority = request.Priority.Value;
        if (request.AssigneeId is not null) ticket.AssigneeId = request.AssigneeId;

        var changedFields = new List<string>();
        if (request.Status is not null) changedFields.Add($"status → {ticket.Status}");
        if (request.Priority is not null) changedFields.Add($"priority → {ticket.Priority}");
        if (request.AssigneeId is not null) changedFields.Add("assignee");
        if (changedFields.Count > 0)
        {
            _auditLog.Record(tenantId, actorUserId: null, ActorLabel, AuditAction.Updated,
                AuditEntityType.Ticket, ticket.Id, $"Updated ticket '{ticket.Subject}' via public API: {string.Join(", ", changedFields)}.");
        }

        await _ruleEngine.RunTriggerAsync(tenantId, AutomationTrigger.TicketUpdated, ticket, ct);

        if (ticket.AssigneeId is not null && ticket.AssigneeId != previousAssigneeId)
        {
            await _notifications.NotifyUserAsync(tenantId, ticket.AssigneeId.Value, NotificationType.TicketAssigned,
                $"You were assigned '{ticket.Subject}'.", ticket.Id, ct);
        }

        // Fires regardless of whether the rule engine above is what actually
        // changed the status vs. this request's own Status field - either
        // way the ticket's status is different now than it was when this
        // request started, which is the event a subscriber cares about.
        if (ticket.Status != previousStatus)
        {
            await _webhooks.NotifyTicketStatusChangedAsync(tenantId, ticket, previousStatus, ct);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(PublicTicketResponse.FromEntity(ticket));
    }
}
