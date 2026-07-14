using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Sla;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

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
    private readonly IAuditLogService _auditLog;

    public SlaPoliciesController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SlaPolicyResponse>>> GetPolicies(CancellationToken ct)
    {
        var policies = await _db.SlaPolicies.OrderBy(p => p.Name).ToListAsync(ct);
        return Ok(policies.Select(SlaPolicyResponse.FromEntity));
    }

    [HttpPost]
    [Authorize(Policy = "Permission:ManageSlaPolicies")]
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

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.SlaPolicy, policy.Id, $"Created SLA policy '{policy.Name}'.");

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetPolicies), SlaPolicyResponse.FromEntity(policy));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "Permission:ManageSlaPolicies")]
    public async Task<ActionResult<SlaPolicyResponse>> UpdatePolicy(Guid id, [FromBody] UpdateSlaPolicyRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();

        if (request.Name is not null) policy.Name = request.Name;
        if (request.ResponseTargetMinutes is not null) policy.ResponseTargetMinutes = request.ResponseTargetMinutes.Value;
        if (request.ResolutionTargetMinutes is not null) policy.ResolutionTargetMinutes = request.ResolutionTargetMinutes.Value;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.SlaPolicy, policy.Id, $"Updated SLA policy '{policy.Name}'.");

        await _db.SaveChangesAsync(ct);
        return Ok(SlaPolicyResponse.FromEntity(policy));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Permission:ManageSlaPolicies")]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();

        // Tickets already assigned to this policy keep their SlaPolicyId (now
        // dangling) and their already-computed due dates - deleting a policy
        // doesn't retroactively change commitments already made to existing tickets.
        _db.SlaPolicies.Remove(policy);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.SlaPolicy, policy.Id, $"Deleted SLA policy '{policy.Name}'.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
