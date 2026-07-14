using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Assets;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 10 - Asset Management/CMDB. Gated behind Tenant.CmdbEnabled (a
// per-tenant feature flag toggled by a Super Admin - see
// SuperAdminTenantsController.UpdateFeatureFlags), independent of Plan,
// matching the spec's framing as an "Enterprise add-on" a WMX operator turns
// on per negotiated deal, not something every tenant on an Enterprise-priced
// Plan automatically gets.
[ApiController]
[Route("api/assets")]
[Authorize(Policy = "TenantStaff")]
public class AssetsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;

    public AssetsController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AssetResponse>>> GetAssets(
        [FromQuery] AssetType? type, [FromQuery] AssetStatus? status, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var query = _db.Assets.AsQueryable();
        if (type is not null) query = query.Where(a => a.Type == type);
        if (status is not null) query = query.Where(a => a.Status == status);

        var assets = await query.OrderBy(a => a.Name).ToListAsync(ct);
        return Ok(assets.Select(AssetResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AssetResponse>> GetAsset(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();
        return Ok(AssetResponse.FromEntity(asset));
    }

    [HttpPost]
    [Authorize(Policy = "Permission:ManageAssets")]
    public async Task<ActionResult<AssetResponse>> CreateAsset([FromBody] CreateAssetRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var utcNow = DateTime.UtcNow;
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Type = request.Type,
            Status = AssetStatus.Active,
            SerialNumberOrLicenseKey = request.SerialNumberOrLicenseKey,
            AssignedToUserId = request.AssignedToUserId,
            Location = request.Location,
            PurchaseDate = request.PurchaseDate,
            WarrantyExpiresAt = request.WarrantyExpiresAt,
            Notes = request.Notes,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

        _db.Assets.Add(asset);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.Asset, asset.Id, $"Created asset '{asset.Name}'.");

        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAsset), new { id = asset.Id }, AssetResponse.FromEntity(asset));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "Permission:ManageAssets")]
    public async Task<ActionResult<AssetResponse>> UpdateAsset(Guid id, [FromBody] UpdateAssetRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();

        if (request.Name is not null) asset.Name = request.Name;
        if (request.Type is not null) asset.Type = request.Type.Value;
        if (request.Status is not null) asset.Status = request.Status.Value;
        if (request.SerialNumberOrLicenseKey is not null) asset.SerialNumberOrLicenseKey = request.SerialNumberOrLicenseKey;
        if (request.AssignedToUserId is not null) asset.AssignedToUserId = request.AssignedToUserId;
        if (request.Location is not null) asset.Location = request.Location;
        if (request.PurchaseDate is not null) asset.PurchaseDate = request.PurchaseDate;
        if (request.WarrantyExpiresAt is not null) asset.WarrantyExpiresAt = request.WarrantyExpiresAt;
        if (request.Notes is not null) asset.Notes = request.Notes;
        asset.UpdatedAt = DateTime.UtcNow;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.Asset, asset.Id, $"Updated asset '{asset.Name}'.");

        await _db.SaveChangesAsync(ct);
        return Ok(AssetResponse.FromEntity(asset));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Permission:ManageAssets")]
    public async Task<IActionResult> DeleteAsset(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();

        // Unlike KnowledgeArticleVersion/AutomationRuleLog's "dangling
        // reference is harmless history" convention, a TicketAsset link
        // pointing at a deleted asset has no record left to describe (the
        // asset's own fields are gone with it) - so these are actually
        // removed, not left dangling.
        var links = await _db.TicketAssets.Where(l => l.AssetId == id).ToListAsync(ct);
        _db.TicketAssets.RemoveRange(links);
        _db.Assets.Remove(asset);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.Asset, asset.Id, $"Deleted asset '{asset.Name}'.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/tickets")]
    public async Task<ActionResult<IEnumerable<AssetTicketSummary>>> GetAssetTickets(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var assetExists = await _db.Assets.AnyAsync(a => a.Id == id, ct);
        if (!assetExists) return NotFound();

        var ticketIds = await _db.TicketAssets.Where(l => l.AssetId == id).Select(l => l.TicketId).ToListAsync(ct);
        var tickets = await _db.Tickets
            .Where(t => ticketIds.Contains(t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Ok(tickets.Select(t => new AssetTicketSummary(t.Id, t.Subject, t.Status.ToString(), t.Priority.ToString(), t.CreatedAt)));
    }

    // Linking/unlinking an existing asset to a ticket is routine day-to-day
    // ticket work (like assigning a ticket to an agent), not a config
    // action - open to any tenant staff, not gated by ManageAssets.
    [HttpPost("{id:guid}/tickets")]
    public async Task<IActionResult> LinkTicket(Guid id, [FromBody] LinkTicketRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound(new { message = "Asset not found." });

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == request.TicketId, ct);
        if (ticket is null) return NotFound(new { message = "Ticket not found." });

        var alreadyLinked = await _db.TicketAssets.AnyAsync(l => l.AssetId == id && l.TicketId == request.TicketId, ct);
        if (alreadyLinked) return Conflict(new { message = "This ticket is already linked to this asset." });

        _db.TicketAssets.Add(new TicketAsset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TicketId = request.TicketId,
            AssetId = id,
            LinkedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The AnyAsync check above isn't atomic with this insert - two
            // concurrent LinkTicket calls for the same (asset, ticket) pair
            // can both pass the check before either commits. The unique
            // index on (AssetId, TicketId) (see TmsDbContext) catches the
            // loser at the database level; without this catch, that surfaces
            // as a raw unhandled 500 instead of the same clean 409 the
            // non-racing path already returns.
            return Conflict(new { message = "This ticket is already linked to this asset." });
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}/tickets/{ticketId:guid}")]
    public async Task<IActionResult> UnlinkTicket(Guid id, Guid ticketId, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await IsCmdbEnabledAsync(tenantId, ct)) return CmdbDisabled();

        var link = await _db.TicketAssets.FirstOrDefaultAsync(l => l.AssetId == id && l.TicketId == ticketId, ct);
        if (link is null) return NotFound();

        _db.TicketAssets.Remove(link);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> IsCmdbEnabledAsync(Guid tenantId, CancellationToken ct)
    {
        // Tenants isn't query-filtered (see TmsDbContext), so this reads
        // exactly the current tenant's own row - not a cross-tenant read.
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return tenant?.CmdbEnabled ?? false;
    }

    private ObjectResult CmdbDisabled() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Asset Management (CMDB) isn't enabled for this workspace yet - contact WMX to turn it on." });
}
