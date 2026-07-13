namespace Tms.Api.Models;

public enum TenantStatus { Trial, Active, PastDue, Suspended, Churned }

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TrialEndsAt { get; set; }

    // Setup-wizard fields (Module 2). Nullable/defaulted so existing rows
    // (seeded before this migration) don't need a backfill.
    public string TimeZone { get; set; } = "UTC";
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
}
