using Tms.Api.Models;

namespace Tms.Api.Dtos.Audit;

public record AuditLogResponse(
    Guid Id,
    string ActorLabel,
    AuditAction Action,
    AuditEntityType EntityType,
    Guid EntityId,
    string Summary,
    DateTime Timestamp)
{
    public static AuditLogResponse FromEntity(AuditLog log) => new(
        log.Id,
        log.ActorLabel,
        log.Action,
        log.EntityType,
        log.EntityId,
        log.Summary,
        log.Timestamp);
}
