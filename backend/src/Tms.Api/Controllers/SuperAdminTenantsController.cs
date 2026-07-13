using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

public record CreateTenantRequest(string Name, string Subdomain, Guid PlanId, int TrialDays = 14);

// Platform-only endpoints (Super Admin console, Module 5.1 - Tenant Lifecycle
// Management). Protected by PlatformUser auth exclusively - a tenant AppUser's
// JWT has no "scope=platform_admin" claim so it can never satisfy these.
[ApiController]
[Route("api/platform/tenants")]
[Authorize(Policy = "PlatformAdmin")]
public class SuperAdminTenantsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public SuperAdminTenantsController(TmsDbContext db)
    {
        _db = db;
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
}
