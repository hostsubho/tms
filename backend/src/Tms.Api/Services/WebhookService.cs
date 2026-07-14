using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Services;

// Module 11 - Integrations & Public API. Delivers outbound webhooks
// synchronously, inline within whichever request triggered the event
// (ticket create/status-change) - there is no background worker/queue in
// this deployment (the same constraint already accepted everywhere else in
// this codebase), so a slow or unreachable subscriber adds directly to that
// request's latency. Tenants with zero configured webhooks (the common
// case) pay only the cost of one indexed, empty query.
public class WebhookService : IWebhookService
{
    private readonly TmsDbContext _db;
    private readonly HttpClient _httpClient;

    public WebhookService(TmsDbContext db, HttpClient httpClient)
    {
        _db = db;
        _httpClient = httpClient;
    }

    public Task NotifyTicketCreatedAsync(Guid tenantId, Ticket ticket, CancellationToken ct) =>
        DeliverAsync(tenantId, WebhookEvent.TicketCreated, ticket, previousStatus: null, ct);

    public Task NotifyTicketStatusChangedAsync(Guid tenantId, Ticket ticket, TicketStatus previousStatus, CancellationToken ct) =>
        DeliverAsync(tenantId, WebhookEvent.TicketStatusChanged, ticket, previousStatus, ct);

    private async Task DeliverAsync(Guid tenantId, WebhookEvent evt, Ticket ticket, TicketStatus? previousStatus, CancellationToken ct)
    {
        // TenantId filtered explicitly here, on top of the DbContext's own
        // global query filter (which relies on the ambient ITenantContext) -
        // belt-and-suspenders so this query can never drift from the
        // tenantId the caller actually passed in, even if some future
        // change decoupled the two.
        var subscriptions = await _db.WebhookSubscriptions
            .Where(w => w.TenantId == tenantId && w.Event == evt && w.IsActive)
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var payload = JsonSerializer.Serialize(new
        {
            @event = evt.ToString(),
            ticketId = ticket.Id,
            subject = ticket.Subject,
            status = ticket.Status.ToString(),
            priority = ticket.Priority.ToString(),
            previousStatus = previousStatus?.ToString(),
            timestamp = DateTime.UtcNow,
        });

        foreach (var subscription in subscriptions)
        {
            var log = new WebhookDeliveryLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WebhookSubscriptionId = subscription.Id,
                TicketId = ticket.Id,
                Event = evt,
                AttemptedAt = DateTime.UtcNow,
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-Tms-Signature", ComputeSignature(subscription.Secret, payload));

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                log.Success = response.IsSuccessStatusCode;
                log.StatusCode = (int)response.StatusCode;
            }
            catch (Exception ex)
            {
                // Deliberately broad - a subscriber's DNS failure, connection
                // refusal, TLS error, or timeout should all just be recorded
                // as a failed delivery, never bubble up and fail the ticket
                // create/update that triggered it.
                log.Success = false;
                log.Error = ex.Message;
            }

            _db.WebhookDeliveryLogs.Add(log);
        }

        // Not saved here - the calling controller's own SaveChangesAsync
        // persists these delivery logs in the same transaction as the
        // ticket change that triggered them, the same convention already
        // used by IAuditLogService.Record / AutomationRuleLog.
    }

    private static string ComputeSignature(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
