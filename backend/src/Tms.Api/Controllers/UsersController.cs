using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Users;

namespace Tms.Api.Controllers;

// Minimal tenant staff directory - no invite/deactivate/role-change endpoints
// here yet (that's user management, a separate concern). This exists so
// UIs that need to let someone pick a specific teammate - the Module 5
// automation rule builder's "assign to agent" action, for a start - have
// something to list against instead of requiring a hand-typed GUID.
[ApiController]
[Route("api/users")]
[Authorize(Policy = "TenantStaff")]
public class UsersController : ControllerBase
{
    private readonly TmsDbContext _db;

    public UsersController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetUsers(CancellationToken ct)
    {
        var users = await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        return Ok(users.Select(UserResponse.FromEntity));
    }
}
