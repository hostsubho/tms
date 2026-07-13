using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Onboarding;

namespace Tms.Api.Controllers;

// Self-service tenant settings (Module 2 - setup wizard fields, plan
// upgrade/downgrade). Reachable with a regular tenant AppUser JWT, unlike
// /api/platform/tenants which requires a PlatformUser token. Resolves "which
// tenant" from ITenantContext (set by TenantResolutionMiddleware from the
// tenant_id claim), the same as every other tenant-scoped controller.
[ApiController]
[Route("api/tenant")]
[Authorize]
public class TenantController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TenantController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet("me")]
    public async Task<ActionResult<TenantSettingsResponse>> GetMyTenant(CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        return tenant is null ? NotFound() : Ok(ToResponse(tenant));
    }

    [HttpPatch("me")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TenantSettingsResponse>> UpdateMyTenant([FromBody] UpdateTenantSettingsRequest request, CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        if (request.Name is not null) tenant.Name = request.Name;
        if (request.TimeZone is not null) tenant.TimeZone = request.TimeZone;
        if (request.LogoUrl is not null) tenant.LogoUrl = request.LogoUrl;
        if (request.PrimaryColor is not null) tenant.PrimaryColor = request.PrimaryColor;

        await _db.SaveChangesAsync(ct);
        return Ok(ToResponse(tenant));
    }

    [HttpPost("me/plan")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TenantSettingsResponse>> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        var planExists = await _db.Plans.AnyAsync(p => p.Id == request.PlanId, ct);
        if (!planExists)
        {
            return BadRequest(new { message = "Unknown planId. Call GET /api/plans for valid options." });
        }

        tenant.PlanId = request.PlanId;
        await _db.SaveChangesAsync(ct);
        return Ok(ToResponse(tenant));
    }

    private async Task<Models.Tenant?> GetOwnTenantAsync(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        // Tenants has no query filter (Super Admin queries it unfiltered too),
        // so this explicitly scopes to the caller's own tenant only.
        return await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    }

    private static TenantSettingsResponse ToResponse(Models.Tenant t) => new(
        t.Id, t.Name, t.Subdomain, t.TimeZone, t.LogoUrl, t.PrimaryColor,
        t.PlanId, t.Status.ToString(), t.TrialEndsAt);
}
