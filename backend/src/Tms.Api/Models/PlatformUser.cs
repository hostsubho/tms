namespace Tms.Api.Models;

// Roles for the Super Admin console (Module 5.6). Entirely separate from the
// tenant-scoped Role enum on AppUser - a PlatformUser is never a member of
// any tenant and must never be reachable via tenant-scoped auth.
public enum PlatformRole
{
    Owner,
    PlatformAdmin,
    SupportEngineer,
    BillingAdmin,
    ReadOnlyAnalyst
}

public class PlatformUser
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public PlatformRole Role { get; set; } = PlatformRole.ReadOnlyAnalyst;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
