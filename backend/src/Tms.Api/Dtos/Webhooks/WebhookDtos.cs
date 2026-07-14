using Tms.Api.Models;

namespace Tms.Api.Dtos.Webhooks;

public record CreateWebhookRequest(string Url, WebhookEvent Event);
public record UpdateWebhookRequest(bool? IsActive);

public record WebhookResponse(
    Guid Id,
    string Url,
    WebhookEvent Event,
    bool IsActive,
    DateTime CreatedAt)
{
    public static WebhookResponse FromEntity(WebhookSubscription w) => new(
        w.Id, w.Url, w.Event, w.IsActive, w.CreatedAt);
}

// Secret returned exactly once, at creation - see WebhooksController.CreateWebhook.
public record CreatedWebhookResponse(
    Guid Id,
    string Url,
    WebhookEvent Event,
    string Secret,
    DateTime CreatedAt);

public record WebhookDeliveryLogResponse(
    Guid Id,
    Guid TicketId,
    WebhookEvent Event,
    bool Success,
    int? StatusCode,
    string? Error,
    DateTime AttemptedAt)
{
    public static WebhookDeliveryLogResponse FromEntity(WebhookDeliveryLog l) => new(
        l.Id, l.TicketId, l.Event, l.Success, l.StatusCode, l.Error, l.AttemptedAt);
}
