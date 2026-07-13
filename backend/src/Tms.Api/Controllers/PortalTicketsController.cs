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

    public PortalTicketsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PortalTicketResponse>>> GetMyTickets(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();

        var tickets = await _db.Tickets
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var utcNow = DateTime.UtcNow;
        var escalatedAny = false;
        foreach (var ticket in tickets)
        {
            if (SlaEvaluator.CheckAndEscalate(ticket, utcNow)) escalatedAny = true;
        }
        if (escalatedAny) await _db.SaveChangesAsync(ct);

        return Ok(tickets.Select(PortalTicketResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortalTicketResponse>> GetMyTicket(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);
        if (ticket is null) return NotFound();

        var utcNow = DateTime.UtcNow;
        if (SlaEvaluator.CheckAndEscalate(ticket, utcNow)) await _db.SaveChangesAsync(ct);

        return Ok(PortalTicketResponse.FromEntity(ticket));
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
