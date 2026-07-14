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
