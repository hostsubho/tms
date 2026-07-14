using Tms.Api.Models;

namespace Tms.Api.Dtos.Notifications;

public record NotificationResponse(Guid Id, NotificationType Type, string Message, Guid TicketId, bool IsRead, DateTime CreatedAt)
{
    public static NotificationResponse FromEntity(Notification n) => new(
        n.Id, n.Type, n.Message, n.TicketId, n.IsRead, n.CreatedAt);
}

public record UpdateNotificationPreferenceRequest(bool Enabled);

public record NotificationPreferenceResponse(bool Enabled);
