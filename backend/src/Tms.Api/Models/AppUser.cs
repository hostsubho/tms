namespace Tms.Api.Models;

// MVP roles for Phase 1 (see feature spec, Module 12). A full custom-role/
// permission-set model comes in Phase 3; until then this fixed enum is the
// source of truth for authorization checks.
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
}
