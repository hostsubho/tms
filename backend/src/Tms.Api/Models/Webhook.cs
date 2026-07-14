namespace Tms.Api.Models;

public enum WebhookEvent
{
    TicketCreated,
    TicketStatusChanged,
}

// Module 11 - Integrations & Public API. Outbound notification of ticket
// events to an external URL the tenant controls, delivered synchronously
// in-request (see WebhookService) - there is no background worker/queue in
// this deployment, the same constraint already accepted for SLA breach
// checks and notifications elsewhere in this codebase.
public class WebhookSubscription
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Url { get; set; } = string.Empty;
    public WebhookEvent Event { get; set; }

    // Stored in reversible plaintext (unlike ApiKey.KeyHash) because the
    // same secret must be reused on every delivery to compute the HMAC
    // signature - there's no "verify once then discard" the way a login
    // credential works.
    public string Secret { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Append-only delivery history - kept even if the parent subscription is
// later deleted (dangling WebhookSubscriptionId, same convention as
// AutomationRuleLog surviving its rule's deletion) so an admin can still
// review what fired before a webhook was removed.
public class WebhookDeliveryLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public Guid TicketId { get; set; }
    public WebhookEvent Event { get; set; }
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
