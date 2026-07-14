using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Platform;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

public record CreateTenantRequest(string Name, string Subdomain, Guid PlanId, int TrialDays = 14);

// Module 10 / Super Admin 5.1 - "Tenant-level feature flags." Scoped to
// exactly the one flag that exists today (CmdbEnabled) rather than a
// generic name/value flag store - see Tenant.CmdbEnabled's own comment for
// why. Shaped as an object (not a bare bool) so a future second flag can be
// added to the same request without another endpoint.
public record UpdateFeatureFlagsRequest(bool CmdbEnabled);

// Platform-only endpoints (Super Admin console, Module 5.1 - Tenant Lifecycle
// Management). Protected by PlatformUser auth exclusively - a tenant AppUser's
// JWT has no "scope=platform_admin" claim so it can never satisfy these.
[ApiController]
[Route("api/platform/tenants")]
[Authorize(Policy = "PlatformAdmin")]
public class SuperAdminTenantsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly IJwtTokenService _tokenService;
    private readonly IAuditLogService _auditLog;

    public SuperAdminTenantsController(TmsDbContext db, IJwtTokenService tokenService, IAuditLogService auditLog)
    {
        _db = db;
        _tokenService = tokenService;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Tenant>>> GetAllTenants(CancellationToken ct)
    {
        var tenants = await _db.Tenants.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return Ok(tenants);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Tenant>> GetTenant(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    // Creation, suspension, and reactivation are gated behind the stricter
    // PlatformManage policy (Owner/PlatformAdmin only) - a SupportEngineer or
    // ReadOnlyAnalyst platform token can list/view tenants but not mutate them.
    [HttpPost]
    [Authorize(Policy = "PlatformManage")]
    public async Task<ActionResult<Tenant>> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        var subdomainTaken = await _db.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain, ct);
        if (subdomainTaken)
        {
            return Conflict(new { message = "That subdomain is already in use." });
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Subdomain = request.Subdomain,
            PlanId = request.PlanId,
            Status = TenantStatus.Trial,
            CreatedAt = DateTime.UtcNow,
            TrialEndsAt = DateTime.UtcNow.AddDays(request.TrialDays),
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = "PlatformManage")]
    public async Task<IActionResult> SuspendTenant(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        tenant.Status = TenantStatus.Suspended;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "PlatformManage")]
    public async Task<IActionResult> ReactivateTenant(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        if (tenant.Status is not (TenantStatus.Suspended or TenantStatus.PastDue))
        {
            return BadRequest(new { message = $"Cannot reactivate a tenant with status '{tenant.Status}'." });
        }

        tenant.Status = TenantStatus.Active;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/feature-flags")]
    [Authorize(Policy = "PlatformManage")]
    public async Task<ActionResult<Tenant>> UpdateFeatureFlags(Guid id, [FromBody] UpdateFeatureFlagsRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        tenant.CmdbEnabled = request.CmdbEnabled;
        await _db.SaveChangesAsync(ct);
        return Ok(tenant);
    }

    // Module 5.1 - Tenant impersonation. Restricted to PlatformImpersonate
    // (Owner/PlatformAdmin/SupportEngineer) - narrower than PlatformAdmin
    // (any role, used for the reads above) since this issues a token that
    // grants full access to a customer's private ticket data.
    [HttpPost("{id:guid}/impersonate")]
    [Authorize(Policy = "PlatformImpersonate")]
    public async Task<ActionResult<ImpersonateResponse>> Impersonate(Guid id, [FromBody] ImpersonateRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        // Deliberately NOT blocked for Suspended/Churned tenants (unlike
        // AuthController.Login, which does block those) - a support
        // engineer needs to be able to impersonate exactly the tenants
        // most likely to need help: one that just got suspended for
        // non-payment, or is disputing why it was suspended at all. This is
        // a conscious product decision, not an oversight - every such
        // session is still fully logged (ImpersonationLog + the tenant's
        // own AuditLog below), so it trades "can't touch a suspended
        // tenant" for "fully accountable if you do."

        // Users carries a TenantId query filter keyed off ITenantContext,
        // which is never set for a platform-scoped request (PlatformUser
        // tokens carry no tenant_id claim) - IgnoreQueryFilters() plus an
        // explicit TenantId check here is the same pattern
        // PlatformBillingController already uses for exactly this reason.
        // IsActive is checked the same way AuthController.Login checks it -
        // a deactivated/offboarded AppUser must not be impersonatable, the
        // same as they can't log in themselves anymore.
        AppUser? targetUser;
        if (request.UserId is not null)
        {
            targetUser = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == request.UserId && u.TenantId == id && u.IsActive, ct);
            if (targetUser is null)
            {
                return NotFound(new { message = "That user doesn't belong to this tenant, or is no longer active." });
            }
        }
        else
        {
            // Default: the tenant's own earliest-created *active* Admin -
            // the common "debug an issue for this customer" case, needing
            // no extra input from the Super Admin.
            targetUser = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == id && u.Role == Role.Admin && u.IsActive)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (targetUser is null)
            {
                return NotFound(new { message = "This tenant has no active Admin user to impersonate." });
            }
        }

        var permissions = targetUser.CustomRoleId is null
            ? Array.Empty<Permission>()
            : await _db.CustomRolePermissions.IgnoreQueryFilters()
                .Where(p => p.CustomRoleId == targetUser.CustomRoleId)
                .Select(p => p.Permission)
                .ToArrayAsync(ct);

        var platformUserEmail = User.GetEmail();
        var accessToken = _tokenService.CreateAccessToken(targetUser, permissions, impersonatorEmail: platformUserEmail);

        // Platform-side record (see ImpersonationLog's own comment for why
        // this is a dedicated table, not the full cross-tenant audit log).
        _db.ImpersonationLogs.Add(new ImpersonationLog
        {
            Id = Guid.NewGuid(),
            PlatformUserId = User.GetUserId(),
            PlatformUserEmail = platformUserEmail,
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            TargetUserId = targetUser.Id,
            TargetUserEmail = targetUser.Email,
            StartedAt = DateTime.UtcNow,
        });

        // Also visible in the tenant's own audit log, per the spec's own
        // wording ("visible to the tenant's audit log too") - actorUserId
        // is null since this wasn't done by any of the tenant's own users;
        // the label makes clear a WMX Super Admin started this.
        _auditLog.Record(tenant.Id, actorUserId: null, $"Super Admin: {platformUserEmail}", AuditAction.Created,
            AuditEntityType.Impersonation, targetUser.Id,
            $"Started impersonating this workspace as {targetUser.Email} ({targetUser.Role}).");

        await _db.SaveChangesAsync(ct);

        return Ok(new ImpersonateResponse(
            accessToken.Token, accessToken.ExpiresAtUtc, tenant.Id, tenant.Name, tenant.Subdomain,
            targetUser.Id, targetUser.Email, targetUser.Role.ToString(),
            permissions.Select(p => p.ToString()).ToList()));
    }

    // "Module Licensing" - client customization & module cost negotiation.
    // Read is open to any platform role (PlatformAdmin, class-level policy);
    // GET always returns every ModuleKey, synthesizing a default row
    // (Enabled, no explicit price) for any module the Owner hasn't touched
    // yet - same "stable shape even with nothing stored" convention
    // SsoConfigController.GetConfig uses.
    [HttpGet("{id:guid}/module-flags")]
    public async Task<ActionResult<TenantModuleLicensingResponse>> GetModuleLicensing(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == tenant.PlanId, ct);
        var basePlanCents = (long)Math.Round((plan?.PriceMonthly ?? 0m) * 100m);

        var existingFlags = await _db.TenantModuleFlags.Where(f => f.TenantId == id).ToListAsync(ct);
        var flagsByKey = existingFlags.ToDictionary(f => f.ModuleKey);

        var modules = new List<ModuleFlagResponse>();
        long suggestedModulesCents = 0;
        foreach (var key in Enum.GetValues<ModuleKey>())
        {
            if (flagsByKey.TryGetValue(key, out var flag))
            {
                modules.Add(new ModuleFlagResponse(key.ToString(), flag.Enabled, flag.MonthlyCostCents));
                if (flag.Enabled) suggestedModulesCents += flag.MonthlyCostCents ?? 0;
            }
            else
            {
                // Same default-enabled-except-Cmdb fallback as
                // IModuleAccessService - kept in sync deliberately so this
                // read-only view always matches what IsEnabledAsync would
                // actually decide for a request against this tenant.
                var defaultEnabled = key != ModuleKey.Cmdb || tenant.CmdbEnabled;
                modules.Add(new ModuleFlagResponse(key.ToString(), defaultEnabled, null));
            }
        }

        var suggestedTotal = basePlanCents + suggestedModulesCents;
        var effectiveTotal = tenant.ModuleBillingTotalOverrideCents ?? suggestedTotal;

        return Ok(new TenantModuleLicensingResponse(
            modules, basePlanCents, suggestedTotal, tenant.ModuleBillingTotalOverrideCents, effectiveTotal));
    }

    // Owner/PlatformAdmin only (PlatformManage) - turning a module on or off
    // is a functionality decision for the client, not purely a billing one,
    // so this is gated the same as the legacy CmdbEnabled toggle above
    // rather than the narrower PlatformBilling policy used for the
    // total-override endpoint below. Setting a price alongside Enabled in
    // the same call is still allowed here (an Owner does both in one motion
    // when onboarding a module for a client) - BillingAdmin just can't flip
    // Enabled unilaterally.
    [HttpPut("{id:guid}/module-flags/{moduleKey}")]
    [Authorize(Policy = "PlatformManage")]
    public async Task<ActionResult<ModuleFlagResponse>> UpdateModuleFlag(
        Guid id, ModuleKey moduleKey, [FromBody] UpdateModuleFlagRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        if (request.MonthlyCostCents is < 0)
        {
            return BadRequest(new { message = "monthlyCostCents cannot be negative." });
        }

        var flag = await _db.TenantModuleFlags.FirstOrDefaultAsync(f => f.TenantId == id && f.ModuleKey == moduleKey, ct);
        if (flag is null)
        {
            flag = new TenantModuleFlag { Id = Guid.NewGuid(), TenantId = id, ModuleKey = moduleKey };
            _db.TenantModuleFlags.Add(flag);
        }

        flag.Enabled = request.Enabled;
        flag.MonthlyCostCents = request.MonthlyCostCents;
        flag.UpdatedAt = DateTime.UtcNow;

        // Keep the legacy Tenant.CmdbEnabled column mirrored for Cmdb
        // specifically - see TenantModuleFlag's own doc comment for why
        // (the older Module 10 toggle/UI, and IModuleAccessService's
        // fallback for tenants with no row yet, both still read this).
        if (moduleKey == ModuleKey.Cmdb)
        {
            tenant.CmdbEnabled = request.Enabled;
        }

        var priceNote = request.MonthlyCostCents is not null ? $", priced at ${request.MonthlyCostCents / 100m:F2}/mo" : "";
        _auditLog.Record(id, actorUserId: null, $"Super Admin: {User.GetEmail()}", AuditAction.Updated,
            AuditEntityType.Billing, flag.Id, $"{(request.Enabled ? "Enabled" : "Disabled")} module '{moduleKey}'{priceNote}.");

        await _db.SaveChangesAsync(ct);
        return Ok(new ModuleFlagResponse(moduleKey.ToString(), flag.Enabled, flag.MonthlyCostCents));
    }

    // PlatformBilling (Owner/PlatformAdmin/BillingAdmin) - purely a
    // negotiated-price lever, doesn't touch what the tenant can actually do,
    // so it's scoped the same as ApplyCredit/OverridePlan on
    // PlatformBillingController rather than the stricter PlatformManage used
    // for the module toggle above.
    [HttpPut("{id:guid}/billing-total-override")]
    [Authorize(Policy = "PlatformBilling")]
    public async Task<IActionResult> UpdateBillingTotalOverride(Guid id, [FromBody] UpdateBillingTotalOverrideRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        if (request.TotalOverrideCents is < 0)
        {
            return BadRequest(new { message = "totalOverrideCents cannot be negative." });
        }

        tenant.ModuleBillingTotalOverrideCents = request.TotalOverrideCents;

        _auditLog.Record(id, actorUserId: null, $"Super Admin: {User.GetEmail()}", AuditAction.Updated,
            AuditEntityType.Billing, id, request.TotalOverrideCents is null
                ? "Cleared the negotiated billing total override - back to the computed suggested total."
                : $"Set a negotiated billing total override of ${request.TotalOverrideCents / 100m:F2}/mo.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Absolute route (leading "/") rather than nesting under this
    // controller's "api/platform/tenants" prefix - this isn't scoped to one
    // tenant, it's the platform-wide "who impersonated whom, when" list.
    [HttpGet("/api/platform/impersonation-logs")]
    public async Task<ActionResult<IEnumerable<ImpersonationLogResponse>>> GetImpersonationLogs(CancellationToken ct)
    {
        var logs = await _db.ImpersonationLogs
            .OrderByDescending(l => l.StartedAt)
            .Take(200)
            .ToListAsync(ct);

        return Ok(logs.Select(l => new ImpersonationLogResponse(
            l.Id, l.PlatformUserEmail, l.TenantId, l.TenantName, l.TargetUserEmail, l.StartedAt)));
    }
}
