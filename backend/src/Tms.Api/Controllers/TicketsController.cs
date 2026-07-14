using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
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

    public TicketsController(TmsDbContext db, ITenantContext tenantContext, INotificationService notifications)
    {
        _db = db;
        _tenantContext = tenantContext;
        _notifications = notifications;
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

        // Module 8 - Notifications: a brand new ticket needs someone to
        // triage it. If it was created pre-assigned, tell that assignee
        // directly; otherwise every Admin gets a heads-up.
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

        if (request.Subject is not null) ticket.Subject = request.Subject;
        if (request.Description is not null) ticket.Description = request.Description;
        if (request.Status is not null)
        {
            ticket.Status = request.Status.Value;

            // Module 9 - Reporting & Analytics: stamp/clear ResolvedAt on the
            // same transition rather than in a separate pass, so it can never
            // drift out of sync with Status. Reopening a ticket (moving back
            // to New/Open/Pending) clears it - resolution-time metrics should
            // reflect the ticket's current resolve, not a stale one from
            // before it was reopened.
            if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
            {
                ticket.ResolvedAt ??= DateTime.UtcNow;
            }
            else
            {
                ticket.ResolvedAt = null;
            }
        }
        if (request.Priority is not null) ticket.Priority = request.Priority.Value;
        if (request.CategoryId is not null) ticket.CategoryId = request.CategoryId;
        if (request.AssigneeId is not null) ticket.AssigneeId = request.AssigneeId;

        // Module 8 - Notifications: only fire when the assignee actually
        // changed to someone new - not on every PATCH that happens to
        // re-send the same AssigneeId alongside an unrelated status change.
        if (request.AssigneeId is not null && request.AssigneeId != previousAssigneeId)
        {
            await _notifications.NotifyUserAsync(tenantId, request.AssigneeId.Value, NotificationType.TicketAssigned,
                $"You were assigned '{ticket.Subject}'.", ticket.Id, ct);
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
}
