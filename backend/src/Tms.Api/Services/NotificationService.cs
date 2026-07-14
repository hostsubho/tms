using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Services;

public class NotificationService : INotificationService
{
    private readonly TmsDbContext _db;

    public NotificationService(TmsDbContext db)
    {
        _db = db;
    }

    public async Task NotifyUserAsync(Guid tenantId, Guid userId, NotificationType type, string message, Guid ticketId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !user.NotificationsEnabled) return;

        _db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientUserId = userId,
            Type = type,
            Message = message,
            TicketId = ticketId,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task NotifyCustomerAsync(Guid tenantId, Guid customerId, NotificationType type, string message, Guid ticketId, CancellationToken ct)
    {
        var customer = await _db.PortalCustomers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null || !customer.NotificationsEnabled) return;

        _db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientCustomerId = customerId,
            Type = type,
            Message = message,
            TicketId = ticketId,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task NotifyAdminsAsync(Guid tenantId, NotificationType type, string message, Guid ticketId, CancellationToken ct)
    {
        var admins = await _db.Users
            .Where(u => u.Role == Role.Admin && u.IsActive && u.NotificationsEnabled)
            .ToListAsync(ct);

        foreach (var admin in admins)
        {
            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RecipientUserId = admin.Id,
                Type = type,
                Message = message,
                TicketId = ticketId,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }
}
