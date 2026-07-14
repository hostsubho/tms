using Tms.Api.Models;

namespace Tms.Api.Services;

public interface IAuditLogService
{
    // Synchronous and non-persisting by design, same convention as
    // RuleEngineService writing AutomationRuleLog rows - this just stages an
    // AuditLog row on the shared DbContext; the caller's own SaveChangesAsync
    // (already happening at the end of whatever action triggered this)
    // persists it in the same transaction. No separate SaveChanges call here
    // means an audit entry can never be recorded for a change that itself
    // failed to save.
    void Record(
        Guid tenantId,
        Guid? actorUserId,
        string actorLabel,
        AuditAction action,
        AuditEntityType entityType,
        Guid entityId,
        string summary);
}
