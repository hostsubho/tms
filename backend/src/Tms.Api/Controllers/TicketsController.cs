using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public TicketsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Ticket>>> GetTickets(CancellationToken ct)
    {
        // TenantId filter is applied automatically via the DbContext global query filter.
        var tickets = await _db.Tickets
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return Ok(tickets);
    }

    [HttpPost]
    public async Task<ActionResult<Ticket>> CreateTicket([FromBody] Ticket ticket, CancellationToken ct)
    {
        ticket.Id = Guid.NewGuid();
        ticket.CreatedAt = DateTime.UtcNow;
        // TenantId is set from ITenantContext in a real implementation, never trusted from the request body.

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTickets), new { id = ticket.Id }, ticket);
    }
}
