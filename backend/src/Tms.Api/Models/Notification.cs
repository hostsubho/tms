namespace Tms.Api.Models;

// Module 8 - Notifications & Communication. In-app only for this iteration -
// there's no email/SMS provider configured (would need SMTP/Twilio secrets
// entered by the tenant operator, not something to wire up speculatively)
// and no background worker/queue in this deployment (same constraint as
// Module 4's lazy SLA evaluation). Notifications are created synchronously,
// in the same request/transaction as the event that caused them, by
// NotificationService.
public enum NotificationType { TicketAssigned, NewComment, SlaBreach, NewTicket }

public class Notification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Exactly one of these is set, never both - a notification recipient is
    // either tenant staff (AppUser) or a portal customer (PortalCustomer),
    // mirroring how Ticket.RequesterId/CustomerId are mutually exclusive.
    public Guid? RecipientUserId { get; set; }
    public Guid? RecipientCustomerId { get; set; }

    public NotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid TicketId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
