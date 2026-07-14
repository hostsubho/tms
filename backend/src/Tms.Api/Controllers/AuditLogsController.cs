using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Audit;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

// Module 5.4 - Security & Compliance. Read-only by design - there is no
// write endpoint here because audit rows are only ever produced as a side
// effect of the actions they describe (see the other controllers'
// IAuditLogService.Record calls and RuleEngineService for automation
// firings). Restricted to Admin/Manager rather than the broader TenantStaff
// policy every other staff GET here uses - an org-wide "who did what"
// compliance trail isn't something every agent needs visibility into.
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = "TenantStaff")]
public class AuditLogsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public AuditLogsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<IEnumerable<AuditLogResponse>>> GetLogs(
        [FromQuery] AuditEntityType? entityType,
        [FromQuery] AuditAction? action,
        CancellationToken ct)
    {
        // TenantId filter applied automatically via the DbContext global
        // query filter, same as every other tenant-scoped table.
        var query = _db.AuditLogs.AsQueryable();
        if (entityType is not null) query = query.Where(l => l.EntityType == entityType);
        if (action is not null) query = query.Where(l => l.Action == action);

        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(200)
            .ToListAsync(ct);

        return Ok(logs.Select(AuditLogResponse.FromEntity));
    }
}
