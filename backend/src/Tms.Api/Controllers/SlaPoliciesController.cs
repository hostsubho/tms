using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Sla;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

// Module 4 - SLA Management. Policies are matched to tickets by priority at
// creation time (see SlaEvaluator + TicketsController). Read is open to any
// authenticated tenant user (agents need to see what SLA a ticket is under);
// write is restricted to Admin/Manager, same as Categories.
[ApiController]
[Route("api/sla-policies")]
[Authorize(Policy = "TenantStaff")]
public class SlaPoliciesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SlaPoliciesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SlaPolicyResponse>>> GetPolicies(CancellationToken ct)
    {
        var policies = await _db.SlaPolicies.OrderBy(p => p.Name).ToListAsync(ct);
        return Ok(policies.Select(SlaPolicyResponse.FromEntity));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SlaPolicyResponse>> CreatePolicy([FromBody] CreateSlaPolicyRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var conflict = await _db.SlaPolicies.AnyAsync(p => p.Priority == request.Priority, ct);
        if (conflict)
        {
            var label = request.Priority?.ToString() ?? "default";
            return Conflict(new { message = $"A policy already exists for priority '{label}'. Edit or delete it first." });
        }

        var policy = new SlaPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            ResponseTargetMinutes = request.ResponseTargetMinutes,
            ResolutionTargetMinutes = request.ResolutionTargetMinutes,
            Priority = request.Priority,
        };

        _db.SlaPolicies.Add(policy);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetPolicies), SlaPolicyResponse.FromEntity(policy));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SlaPolicyResponse>> UpdatePolicy(Guid id, [FromBody] UpdateSlaPolicyRequest request, CancellationToken ct)
    {
        var policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();

        if (request.Name is not null) policy.Name = request.Name;
        if (request.ResponseTargetMinutes is not null) policy.ResponseTargetMinutes = request.ResponseTargetMinutes.Value;
        if (request.ResolutionTargetMinutes is not null) policy.ResolutionTargetMinutes = request.ResolutionTargetMinutes.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(SlaPolicyResponse.FromEntity(policy));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken ct)
    {
        var policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();

        // Tickets already assigned to this policy keep their SlaPolicyId (now
        // dangling) and their already-computed due dates - deleting a policy
        // doesn't retroactively change commitments already made to existing tickets.
        _db.SlaPolicies.Remove(policy);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
