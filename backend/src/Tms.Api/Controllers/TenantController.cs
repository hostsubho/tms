using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Onboarding;
using Tms.Api.Models;
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
    private readonly IModuleAccessService _moduleAccess;

    public TenantController(TmsDbContext db, ITenantContext tenantContext, IStripeService stripe, IModuleAccessService moduleAccess)
    {
        _db = db;
        _tenantContext = tenantContext;
        _stripe = stripe;
        _moduleAccess = moduleAccess;
    }

    [HttpGet("me")]
    public async Task<ActionResult<TenantSettingsResponse>> GetMyTenant(CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        return tenant is null ? NotFound() : Ok(await ToResponseAsync(tenant, ct));
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
        return Ok(await ToResponseAsync(tenant, ct));
    }

    // Client customization - theming. Separate endpoint/DTO from
    // UpdateMyTenant above (see UpdateThemeRequest's own doc comment).
    // Admin-only, same as every other tenant-settings mutation here - a
    // Manager/Agent shouldn't be able to change branding every user sees.
    [HttpPatch("me/theme")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TenantSettingsResponse>> UpdateTheme([FromBody] UpdateThemeRequest request, CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        // Colors/CSS are free-form strings (any valid CSS color function,
        // not just hex) - validated loosely (length caps only) rather than
        // parsed, since rejecting a technically-valid-but-unusual CSS color
        // value would be more annoying than helpful for what's ultimately
        // just cosmetic per-tenant styling.
        if (request.CustomCss is not null && request.CustomCss.Length > 20_000)
        {
            return BadRequest(new { message = "customCss is too long (20,000 character limit)." });
        }

        if (request.PrimaryColor is not null) tenant.PrimaryColor = request.PrimaryColor;
        if (request.SecondaryColor is not null) tenant.SecondaryColor = request.SecondaryColor;
        if (request.AccentColor is not null) tenant.AccentColor = request.AccentColor;
        if (request.CustomCss is not null) tenant.CustomCss = request.CustomCss;

        if (request.ThemeMode is not null)
        {
            if (!Enum.TryParse<ThemeMode>(request.ThemeMode, ignoreCase: true, out var themeMode))
            {
                return BadRequest(new { message = $"Unknown themeMode '{request.ThemeMode}'. Must be 'Light', 'Dark', or 'Auto'." });
            }
            tenant.ThemeMode = themeMode;
        }

        if (request.BorderRadius is not null)
        {
            if (!Enum.TryParse<ThemeBorderRadius>(request.BorderRadius, ignoreCase: true, out var borderRadius))
            {
                return BadRequest(new { message = $"Unknown borderRadius '{request.BorderRadius}'. Must be 'None', 'Small', 'Medium', or 'Large'." });
            }
            tenant.BorderRadius = borderRadius;
        }

        if (request.Density is not null)
        {
            if (!Enum.TryParse<ThemeDensity>(request.Density, ignoreCase: true, out var density))
            {
                return BadRequest(new { message = $"Unknown density '{request.Density}'. Must be 'Compact', 'Comfortable', or 'Spacious'." });
            }
            tenant.Density = density;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(tenant, ct));
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
        return Ok(await ToResponseAsync(tenant, ct));
    }

    private async Task<Models.Tenant?> GetOwnTenantAsync(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        // Tenants has no query filter (Super Admin queries it unfiltered too),
        // so this explicitly scopes to the caller's own tenant only.
        return await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    }

    private async Task<TenantSettingsResponse> ToResponseAsync(Models.Tenant t, CancellationToken ct)
    {
        // "Module Licensing" - every module this tenant currently has
        // enabled, using the exact same fallback logic every gated
        // controller uses (IModuleAccessService), so this list can never
        // drift from what a request against this tenant would actually be
        // allowed to do.
        var enabledModules = new List<string>();
        foreach (var key in Enum.GetValues<ModuleKey>())
        {
            if (await _moduleAccess.IsEnabledAsync(t.Id, key, ct))
            {
                enabledModules.Add(key.ToString());
            }
        }

        return new TenantSettingsResponse(
            t.Id, t.Name, t.Subdomain, t.TimeZone, t.LogoUrl, t.PrimaryColor,
            t.PlanId, t.Status.ToString(), t.TrialEndsAt, t.CmdbEnabled,
            enabledModules,
            t.SecondaryColor, t.AccentColor, t.ThemeMode.ToString(), t.BorderRadius.ToString(), t.Density.ToString(), t.CustomCss);
    }
}
