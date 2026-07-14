using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Notifications;
using Tms.Api.Extensions;

namespace Tms.Api.Controllers;

// Module 8 - Notifications. Staff-facing surface - every action is
// additionally scoped to RecipientUserId == the caller's own id, on top of
// the DbContext's TenantId query filter, the same "own records only"
// pattern used by PortalTicketsController for customers.
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "TenantStaff")]
public class NotificationsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public NotificationsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationResponse>>> GetMyNotifications(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var notifications = await _db.Notifications
            .Where(n => n.RecipientUserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Ok(notifications.Select(NotificationResponse.FromEntity));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == userId, ct);
        if (notification is null) return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = User.GetUserId();
        await _db.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<NotificationPreferenceResponse>> GetPreferences(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        return new NotificationPreferenceResponse(user.NotificationsEnabled);
    }

    [HttpPatch("preferences")]
    public async Task<ActionResult<NotificationPreferenceResponse>> UpdatePreferences([FromBody] UpdateNotificationPreferenceRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        user.NotificationsEnabled = request.Enabled;
        await _db.SaveChangesAsync(ct);

        return new NotificationPreferenceResponse(user.NotificationsEnabled);
    }
}
