using Tms.Api.Models;

namespace Tms.Api.Services;

public interface IRuleEngineService
{
    // Evaluates every active rule for this tenant/trigger against `ticket`,
    // mutating it in place and queuing AutomationRuleLog rows for any that
    // matched. Never calls SaveChangesAsync itself - the caller (already
    // mid-transaction for the event that triggered this) persists everything
    // together, same convention as INotificationService.
    Task RunTriggerAsync(Guid tenantId, AutomationTrigger trigger, Ticket ticket, CancellationToken ct);
}
