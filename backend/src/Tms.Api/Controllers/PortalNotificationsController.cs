using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Notifications;
using Tms.Api.Extensions;

namespace Tms.Api.Controllers;

// Module 8 - Notifications, customer portal surface. Mirrors
// NotificationsController exactly, scoped to RecipientCustomerId instead of
// RecipientUserId.
[ApiController]
[Route("api/portal/notifications")]
[Authorize(Policy = "PortalCustomer")]
public class PortalNotificationsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public PortalNotificationsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationResponse>>> GetMyNotifications(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var notifications = await _db.Notifications
            .Where(n => n.RecipientCustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Ok(notifications.Select(NotificationResponse.FromEntity));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientCustomerId == customerId, ct);
        if (notification is null) return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        await _db.Notifications
            .Where(n => n.RecipientCustomerId == customerId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<NotificationPreferenceResponse>> GetPreferences(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var customer = await _db.PortalCustomers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return NotFound();

        return new NotificationPreferenceResponse(customer.NotificationsEnabled);
    }

    [HttpPatch("preferences")]
    public async Task<ActionResult<NotificationPreferenceResponse>> UpdatePreferences([FromBody] UpdateNotificationPreferenceRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var customer = await _db.PortalCustomers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return NotFound();

        customer.NotificationsEnabled = request.Enabled;
        await _db.SaveChangesAsync(ct);

        return new NotificationPreferenceResponse(customer.NotificationsEnabled);
    }
}
