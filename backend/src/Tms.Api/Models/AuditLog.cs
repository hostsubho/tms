namespace Tms.Api.Models;

// Module 5.4 - Security & Compliance / Audit Logging. Deliberately a small,
// flat set of action/entity types rather than a fully generic "any field
// changed to any value" diff log - the spec's bar is "who did what, when,"
// not a full change-history/rollback system (that's what
// KnowledgeArticleVersion already does for articles specifically).
public enum AuditAction
{
    Created,
    Updated,
    Deleted,
}

public enum AuditEntityType
{
    Ticket,
    Category,
    SlaPolicy,
    AutomationRule,
    KnowledgeArticle,
}

// Immutable, append-only - there is deliberately no update/delete endpoint
// anywhere in this codebase for AuditLog rows (see AuditLogsController,
// which only ever exposes GET). ActorLabel is a denormalized snapshot (email,
// or a fixed system label for automation-driven entries) rather than a live
// join to Users, so a row still reads sensibly even if the acting user is
// later deactivated or deleted.
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorLabel { get; set; } = "Unknown";
    public AuditAction Action { get; set; }
    public AuditEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
