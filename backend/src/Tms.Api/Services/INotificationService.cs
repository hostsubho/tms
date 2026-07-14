using Tms.Api.Models;

namespace Tms.Api.Services;

// Module 8 - Notifications. Queues (via DbSet.Add, not SaveChangesAsync -
// see NotificationService) an in-app notification for a recipient who has
// NotificationsEnabled. Callers are expected to call SaveChangesAsync
// themselves afterward, same as every other mutation in a controller
// action - this keeps the notification write in the same transaction as
// the ticket/comment mutation that triggered it, rather than a separate
// round trip that could succeed or fail independently.
public interface INotificationService
{
    Task NotifyUserAsync(Guid tenantId, Guid userId, NotificationType type, string message, Guid ticketId, CancellationToken ct);

    Task NotifyCustomerAsync(Guid tenantId, Guid customerId, NotificationType type, string message, Guid ticketId, CancellationToken ct);

    /// Notifies every active Admin in the tenant - used when there's no
    /// single obvious recipient yet (e.g. a brand new ticket with no
    /// assignee).
    Task NotifyAdminsAsync(Guid tenantId, NotificationType type, string message, Guid ticketId, CancellationToken ct);
}
