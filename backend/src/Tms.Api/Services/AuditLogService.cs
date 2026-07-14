using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Services;

public class AuditLogService : IAuditLogService
{
    private readonly TmsDbContext _db;

    public AuditLogService(TmsDbContext db)
    {
        _db = db;
    }

    public void Record(
        Guid tenantId,
        Guid? actorUserId,
        string actorLabel,
        AuditAction action,
        AuditEntityType entityType,
        Guid entityId,
        string summary)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actorUserId,
            ActorLabel = actorLabel,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            Timestamp = DateTime.UtcNow,
        });
    }
}
