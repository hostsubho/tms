using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Onboarding;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Self-service tenant settings (Module 2 - setup wizard fields, plan
// upgrade/downgrade). Reachable with a regular tenant AppUser JWT, unlike
// /api/platform/tenants which requires a PlatformUser token. Resolves "which
// tenant" from ITenantContext (set by TenantResolutionMiddleware from the
// tenant_id claim), the same as every other tenant-scoped controller.
[ApiController]
[Route("api/tenant")]
[Authorize(Policy = "TenantStaff")]
public class TenantController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IStripeService _stripe;

    public TenantController(TmsDbContext db, ITenantContext tenantContext, IStripeService stripe)
    {
        _db = db;
        _tenantContext = tenantContext;
        _stripe = stripe;
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

    // Module 5.2 - Plans & Billing Administration changed the contract here:
    // this endpoint predates real billing and originally switched PlanId
    // unconditionally, no charge involved either way. Now that
    // BillingController exists and can actually charge a card via Stripe
    // Checkout, letting this endpoint freely move a tenant onto a paid plan
    // with zero payment would be a billing bypass - so it's now restricted
    // to genuinely free plans only (PriceMonthly <= 0). Moving to or between
    // paid plans must go through POST /api/billing/change-plan instead.
    [HttpPost("me/plan")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TenantSettingsResponse>> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (plan is null)
        {
            return BadRequest(new { message = "Unknown planId. Call GET /api/plans for valid options." });
        }

        if (plan.PriceMonthly > 0)
        {
            return BadRequest(new
            {
                message = "This endpoint only switches to a free plan. Use POST /api/billing/change-plan for a paid plan - it handles the actual charge via Stripe.",
            });
        }

        // A tenant switching here FROM a paid plan may already have a live
        // Stripe subscription - it must be cancelled, not just ignored
        // locally, or Stripe keeps billing every cycle for a plan the app
        // no longer grants. Same fix as BillingController.ChangePlan's
        // free-plan branch, kept as a separate copy since this controller
        // doesn't otherwise depend on IStripeService for anything else.
        if (tenant.StripeSubscriptionId is not null)
        {
            await _stripe.CancelSubscriptionAsync(tenant.StripeSubscriptionId, ct);
            tenant.StripeSubscriptionId = null;
            tenant.CurrentPeriodEnd = null;
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
        t.PlanId, t.Status.ToString(), t.TrialEndsAt, t.CmdbEnabled);
}
