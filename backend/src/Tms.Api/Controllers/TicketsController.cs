using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Tickets;
using Tms.Api.Extensions;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TicketsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TicketResponse>>> GetTickets(
        [FromQuery] TicketStatus? status,
        [FromQuery] Guid? assigneeId,
        CancellationToken ct)
    {
        // TenantId filter is applied automatically via the DbContext global query filter.
        var query = _db.Tickets.AsQueryable();
        if (status is not null) query = query.Where(t => t.Status == status);
        if (assigneeId is not null) query = query.Where(t => t.AssigneeId == assigneeId);

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return Ok(tickets.Select(TicketResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketResponse>> GetTicket(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        return ticket is null ? NotFound() : Ok(TicketResponse.FromEntity(ticket));
    }

    [HttpPost]
    public async Task<ActionResult<TicketResponse>> CreateTicket([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

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
            SlaPolicyId = request.SlaPolicyId,
            RequesterId = User.GetUserId(),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTicket), new { id = ticket.Id }, TicketResponse.FromEntity(ticket));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<TicketResponse>> UpdateTicket(Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        if (request.Subject is not null) ticket.Subject = request.Subject;
        if (request.Description is not null) ticket.Description = request.Description;
        if (request.Status is not null) ticket.Status = request.Status.Value;
        if (request.Priority is not null) ticket.Priority = request.Priority.Value;
        if (request.CategoryId is not null) ticket.CategoryId = request.CategoryId;
        if (request.AssigneeId is not null) ticket.AssigneeId = request.AssigneeId;

        await _db.SaveChangesAsync(ct);
        return Ok(TicketResponse.FromEntity(ticket));
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

        var ticketExists = await _db.Tickets.AnyAsync(t => t.Id == id, ct);
        if (!ticketExists) return NotFound();

        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TicketId = id,
            AuthorId = User.GetUserId(),
            Body = request.Body,
            IsInternal = request.IsInternal,
            CreatedAt = DateTime.UtcNow,
        };

        _db.TicketComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetComments), new { id }, CommentResponse.FromEntity(comment));
    }
}
