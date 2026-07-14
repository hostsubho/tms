using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Users;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Minimal tenant staff directory - still no invite/deactivate/base-role-
// change endpoints (that's user management, a separate concern), but as of
// Module 12 it does carry one write action: assigning/removing a custom
// role. This exists so UIs that need to let someone pick a specific
// teammate - the Module 5 automation rule builder's "assign to agent"
// action, for a start - have something to list against instead of
// requiring a hand-typed GUID.
[ApiController]
[Route("api/users")]
[Authorize(Policy = "TenantStaff")]
public class UsersController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;

    public UsersController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetUsers(CancellationToken ct)
    {
        var users = await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        var roleIds = users.Where(u => u.CustomRoleId is not null)
            .Select(u => u.CustomRoleId!.Value).Distinct().ToList();
        var roleNames = roleIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.CustomRoles.Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return Ok(users.Select(u => UserResponse.FromEntity(
            u,
            u.CustomRoleId is null ? null : roleNames.GetValueOrDefault(u.CustomRoleId.Value, "(deleted role)"))));
    }

    // Module 12 - Roles & Permissions. Deliberately Admin-only, not gated by
    // any Permission - granting or revoking a user's own extra permissions
    // is exactly the action a permission holder must never be able to
    // perform on themselves or anyone else, or a custom role with even one
    // permission could be used to bootstrap its way to every permission
    // through this endpoint. Same reasoning as CustomRolesController being
    // entirely Admin-only.
    [HttpPatch("{id:guid}/custom-role")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserResponse>> AssignCustomRole(Guid id, [FromBody] AssignCustomRoleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        string? roleName = null;
        if (request.CustomRoleId is not null)
        {
            var role = await _db.CustomRoles.FirstOrDefaultAsync(r => r.Id == request.CustomRoleId, ct);
            if (role is null) return BadRequest(new { message = "That custom role doesn't exist." });
            roleName = role.Name;
        }

        user.CustomRoleId = request.CustomRoleId;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.User, user.Id,
            request.CustomRoleId is null
                ? $"Removed custom role from {user.Email}."
                : $"Assigned custom role '{roleName}' to {user.Email}.");

        await _db.SaveChangesAsync(ct);
        return Ok(UserResponse.FromEntity(user, roleName));
    }
}
