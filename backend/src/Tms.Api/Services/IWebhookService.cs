using Tms.Api.Models;

namespace Tms.Api.Services;

public interface IWebhookService
{
    Task NotifyTicketCreatedAsync(Guid tenantId, Ticket ticket, CancellationToken ct);
    Task NotifyTicketStatusChangedAsync(Guid tenantId, Ticket ticket, TicketStatus previousStatus, CancellationToken ct);
}
