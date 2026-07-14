namespace Tms.Api.Models;

// Module 5.1 - Tenant Lifecycle Management: "every impersonation session is
// logged and visible to the tenant's audit log too." This is the platform
// side of that ("too" implies a primary record elsewhere, i.e. here) - a
// small, purpose-built log rather than the full generic cross-tenant
// "Global audit log across all tenants" the spec separately calls out under
// 5.4 (that would cover tenant-created/suspended/plan-changed events as
// well, a materially bigger feature deferred to its own pass). This table
// exists specifically so a Super Admin can answer "who impersonated which
// tenant, as whom, and when" - not a general platform audit trail.
//
// No TenantId query filter - like PlatformUser, this is only ever queried
// by platform-scoped controllers, never through the tenant-scoped
// TmsDbContext filter path.
public class ImpersonationLog
{
    public Guid Id { get; set; }
    public Guid PlatformUserId { get; set; }
    public string PlatformUserEmail { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public Guid TargetUserId { get; set; }
    public string TargetUserEmail { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
}
