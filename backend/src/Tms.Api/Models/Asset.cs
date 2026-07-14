namespace Tms.Api.Models;

// Module 10 - Asset Management/CMDB. Scoped down from the full spec (see
// docs/tms_spec.md Module 10): a flat asset record with a type/status, not a
// full configuration-item graph with relationships between assets (e.g.
// "this VM runs on that host") - the spec's own done-when bar only asks for
// "an asset can be linked to an incident ticket and its history is visible
// from the asset record," which doesn't need CI-to-CI relationships to
// satisfy.
public enum AssetType { Hardware, Software }
public enum AssetStatus { Active, InRepair, Retired }

public class Asset
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AssetType Type { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Active;

    // Serial number for hardware, license key for software - one free-text
    // field rather than two separate ones, since an asset is always exactly
    // one Type and never both.
    public string? SerialNumberOrLicenseKey { get; set; }

    // The tenant staff member currently responsible for/using this asset -
    // nullable (an asset can sit unassigned in inventory). References
    // AppUser, not enforced by a DB FK (same convention as
    // Ticket.AssigneeId/RequesterId - both nullable Guid columns with no FK
    // constraint elsewhere in this codebase either).
    public Guid? AssignedToUserId { get; set; }

    public string? Location { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpiresAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Join table linking a Ticket (an incident/request) to an Asset it concerns -
// many-to-many, since one ticket can reference multiple assets (e.g. "both
// monitors on this desk failed") and one asset can appear on many tickets
// over its lifetime (its "history," per the spec's done-when bar).
public class TicketAsset
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Guid AssetId { get; set; }
    public DateTime LinkedAt { get; set; }
}
