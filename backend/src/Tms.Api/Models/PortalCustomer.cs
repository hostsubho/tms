namespace Tms.Api.Models;

// Module 7 - Customer/End-User Portal. A PortalCustomer is an external end
// user who submits and tracks their own tickets - entirely separate from
// AppUser (tenant staff: Admin/Manager/Agent/ReadOnly) the same way
// PlatformUser is separate from AppUser. A customer is never a member of
// tenant staff and can never authenticate against /api/tickets or
// /api/platform/* - tokens minted for them carry "scope=portal_customer"
// instead of a Role claim, enforced by the PortalCustomer policy in
// Program.cs.
public class PortalCustomer
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
