using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

// Platform-only endpoints (Super Admin console). Must be protected by a
// separate "PlatformAdmin" auth scheme/policy - never reachable with a
// regular tenant user's JWT. See Module 5 (Super Admin) in the feature spec.
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

    [HttpPost]
    public async Task<ActionResult<Tenant>> CreateTenant([FromBody] Tenant tenant, CancellationToken ct)
    {
        tenant.Id = Guid.NewGuid();
        tenant.CreatedAt = DateTime.UtcNow;
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAllTenants), new { id = tenant.Id }, tenant);
    }

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> SuspendTenant(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        tenant.Status = TenantStatus.Suspended;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
