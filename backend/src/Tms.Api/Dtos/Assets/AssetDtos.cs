using Tms.Api.Models;

namespace Tms.Api.Dtos.Assets;

public record AssetResponse(
    Guid Id,
    string Name,
    string Type,
    string Status,
    string? SerialNumberOrLicenseKey,
    Guid? AssignedToUserId,
    string? Location,
    DateTime? PurchaseDate,
    DateTime? WarrantyExpiresAt,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static AssetResponse FromEntity(Asset a) => new(
        a.Id, a.Name, a.Type.ToString(), a.Status.ToString(), a.SerialNumberOrLicenseKey,
        a.AssignedToUserId, a.Location, a.PurchaseDate, a.WarrantyExpiresAt, a.Notes, a.CreatedAt, a.UpdatedAt);
}

public record CreateAssetRequest(
    string Name,
    AssetType Type,
    string? SerialNumberOrLicenseKey,
    Guid? AssignedToUserId,
    string? Location,
    DateTime? PurchaseDate,
    DateTime? WarrantyExpiresAt,
    string? Notes);

// Partial update - a null field here means "leave unchanged," same
// convention as UpdateArticleRequest/UpdateTenantSettingsRequest elsewhere
// in this codebase. Known limitation this shares with those: an
// already-set nullable field (e.g. clearing AssignedToUserId back to
// "unassigned") can't be explicitly cleared via this endpoint, only
// reassigned to a different value - not worth a wrapper type for a v1 CMDB.
public record UpdateAssetRequest(
    string? Name,
    AssetType? Type,
    AssetStatus? Status,
    string? SerialNumberOrLicenseKey,
    Guid? AssignedToUserId,
    string? Location,
    DateTime? PurchaseDate,
    DateTime? WarrantyExpiresAt,
    string? Notes);

public record LinkTicketRequest(Guid TicketId);

// Minimal ticket summary for an asset's "history" panel - deliberately not
// the full TicketResponse (SLA fields, escalation state, etc. aren't
// relevant to "what's this asset's incident history").
public record AssetTicketSummary(Guid Id, string Subject, string Status, string Priority, DateTime CreatedAt);

// The reverse view: assets linked to a given ticket, for the ticket detail
// page's "linked assets" panel.
public record LinkedAssetSummary(Guid Id, string Name, string Type, string Status);
