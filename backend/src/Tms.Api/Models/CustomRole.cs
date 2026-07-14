namespace Tms.Api.Models;

// Module 12 - Roles & Permissions (Tenant-Level RBAC), the "custom roles"
// half of the module - the fixed Admin/Manager/Agent/ReadOnly roles (the
// "basic RBAC" already shipped in Phase 1, see Role in AppUser.cs) are
// untouched and keep working exactly as before; this is purely additive.
//
// A Permission is a coarse, per-module capability - "can manage this
// module's config" - not a per-endpoint or per-field ACL. This matches the
// spec's own framing for the done-when bar: "a custom role restricted to
// specific modules." Ticket read/write access itself isn't part of this
// set - every tenant staff member (any Role) can already work tickets;
// what a custom role controls is the same set of "Admin/Manager only"
// config surfaces that existed before this module (Categories, SLA
// Policies, Automation Rules, Knowledge Articles) plus the Audit Log.
public enum Permission
{
    ManageCategories,
    ManageSlaPolicies,
    ManageAutomationRules,
    ManageKnowledgeArticles,
    ViewAuditLog,
}

public class CustomRole
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

// A simple join table rather than a comma-separated column on CustomRole -
// keeps each grant independently indexable/queryable, same shape as any
// other one-to-many relationship in this codebase (e.g.
// KnowledgeArticleVersion to KnowledgeArticle).
public class CustomRolePermission
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CustomRoleId { get; set; }
    public Permission Permission { get; set; }
}
