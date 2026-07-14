namespace Tms.Api.Models;

// MVP roles for Phase 1 (see feature spec, Module 12). This fixed enum
// remains the source of truth for the built-in Admin/Manager/Agent/ReadOnly
// roles and every existing [Authorize(Roles = ...)] check keeps working
// unchanged - the Phase 3 custom-role/permission-set model (CustomRole,
// below) is purely additive on top of it, not a replacement.
public enum Role
{
    Admin,
    Manager,
    Agent,
    ReadOnly
}

// Tenant-scoped user (agents / end users). Platform (Super Admin) users are modeled separately.
public class AppUser
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? SsoSubjectId { get; set; }
    public Role Role { get; set; } = Role.Agent;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Module 8 - Notifications. A single on/off switch rather than
    // per-event-type channel toggles - there's only one channel (in-app) in
    // this iteration, so per-channel granularity would be UI for controls
    // that don't do anything yet.
    public bool NotificationsEnabled { get; set; } = true;

    // Module 12 - Roles & Permissions. A plain, unenforced (no FK
    // constraint) reference to a CustomRole - same "dangling reference
    // allowed" convention as AutomationRuleLog.RuleId - except here a
    // dangling reference is actively prevented rather than tolerated:
    // CustomRolesController.DeleteRole nulls this out for every affected
    // user in the same transaction as the role's deletion, since silently
    // stranding a user's permissions on a deleted role definition would be
    // a correctness problem, not just cosmetic history.
    public Guid? CustomRoleId { get; set; }
}
