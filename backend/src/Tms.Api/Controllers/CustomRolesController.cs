using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Roles;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 12 - Roles & Permissions. Strictly Admin-only - both read and
// write - unlike every other "management" controller in this app
// (SlaPolicies/AutomationRules/KnowledgeArticles are Admin+Manager, and as
// of this module can also be delegated to a permission-holder). Deliberately
// NOT gated by any Permission: this controller defines what permissions
// exist and who holds them, so letting a permission-holder (or Manager)
// touch it would open a privilege-escalation path - a custom role with even
// one granted permission could otherwise be used to create a broader role
// and assign it to itself or anyone else. Only the fixed built-in Admin
// role can manage the RBAC surface itself.
[ApiController]
[Route("api/custom-roles")]
[Authorize(Policy = "TenantStaff")]
[Authorize(Roles = "Admin")]
public class CustomRolesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;

    public CustomRolesController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomRoleResponse>>> GetRoles(CancellationToken ct)
    {
        var roles = await _db.CustomRoles.OrderBy(r => r.Name).ToListAsync(ct);
        if (roles.Count == 0) return Ok(Array.Empty<CustomRoleResponse>());

        var roleIds = roles.Select(r => r.Id).ToList();
        var permissions = await _db.CustomRolePermissions
            .Where(p => roleIds.Contains(p.CustomRoleId))
            .ToListAsync(ct);

        return Ok(roles.Select(r => CustomRoleResponse.FromEntity(
            r, permissions.Where(p => p.CustomRoleId == r.Id).Select(p => p.Permission).ToList())));
    }

    [HttpPost]
    public async Task<ActionResult<CustomRoleResponse>> CreateRole([FromBody] CreateCustomRoleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var role = new CustomRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            CreatedAt = DateTime.UtcNow,
        };
        _db.CustomRoles.Add(role);

        // Permissions is a plain (non-nullable) List<Permission> on the DTO,
        // but a caller omitting the field from the JSON body still
        // deserializes it to null - treat that the same as "no permissions
        // granted" rather than letting it throw a NullReferenceException.
        var distinctPermissions = (request.Permissions ?? new List<Permission>()).Distinct().ToList();
        foreach (var permission in distinctPermissions)
        {
            _db.CustomRolePermissions.Add(new CustomRolePermission
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomRoleId = role.Id,
                Permission = permission,
            });
        }

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.CustomRole, role.Id,
            $"Created custom role '{role.Name}' with permissions: {string.Join(", ", distinctPermissions)}.");

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetRoles), CustomRoleResponse.FromEntity(role, distinctPermissions));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CustomRoleResponse>> UpdateRole(Guid id, [FromBody] UpdateCustomRoleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var role = await _db.CustomRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        if (request.Name is not null) role.Name = request.Name;

        List<Permission> finalPermissions;
        if (request.Permissions is not null)
        {
            // Full replace, not a merge/patch of individual permissions -
            // simpler for the admin UI's checkbox form (send the complete
            // desired set) and for this handler (no add/remove diffing).
            var existing = await _db.CustomRolePermissions.Where(p => p.CustomRoleId == id).ToListAsync(ct);
            _db.CustomRolePermissions.RemoveRange(existing);

            var distinctPermissions = request.Permissions.Distinct().ToList();
            foreach (var permission in distinctPermissions)
            {
                _db.CustomRolePermissions.Add(new CustomRolePermission
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CustomRoleId = id,
                    Permission = permission,
                });
            }
            finalPermissions = distinctPermissions;
        }
        else
        {
            finalPermissions = await _db.CustomRolePermissions
                .Where(p => p.CustomRoleId == id)
                .Select(p => p.Permission)
                .ToListAsync(ct);
        }

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.CustomRole, role.Id, $"Updated custom role '{role.Name}'.");

        await _db.SaveChangesAsync(ct);
        return Ok(CustomRoleResponse.FromEntity(role, finalPermissions));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var role = await _db.CustomRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        var permissions = await _db.CustomRolePermissions.Where(p => p.CustomRoleId == id).ToListAsync(ct);
        _db.CustomRolePermissions.RemoveRange(permissions);

        // Unlike AutomationRuleLog/KnowledgeArticleVersion (where a dangling
        // reference to a deleted parent is harmless history), leaving a
        // user's CustomRoleId pointing at a deleted role would silently
        // strand them with permissions tied to a role definition no admin
        // can review or revoke anymore - so this is actively cleaned up,
        // not left dangling.
        var affectedUsers = await _db.Users.Where(u => u.CustomRoleId == id).ToListAsync(ct);
        foreach (var user in affectedUsers)
        {
            user.CustomRoleId = null;
        }

        _db.CustomRoles.Remove(role);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.CustomRole, role.Id, $"Deleted custom role '{role.Name}'.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
