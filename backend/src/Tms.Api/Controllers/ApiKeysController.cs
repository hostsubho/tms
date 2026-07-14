using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.ApiKeys;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 11 - Integrations & Public API. Admin-only, same reasoning as
// CustomRolesController: an API key is a live credential granting broad
// tenant-wide access to the public REST API (create/read/update tickets) -
// at least as sensitive as the RBAC surface itself, not something a Manager
// or permission-holder should be able to mint unilaterally.
[ApiController]
[Route("api/api-keys")]
[Authorize(Policy = "TenantStaff")]
[Authorize(Roles = "Admin")]
public class ApiKeysController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;
    private readonly IModuleAccessService _moduleAccess;

    public ApiKeysController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog, IModuleAccessService moduleAccess)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
        _moduleAccess = moduleAccess;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiKeyResponse>>> GetKeys(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.IntegrationsApi, ct)) return ModuleDisabled();

        var keys = await _db.ApiKeys.OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
        return Ok(keys.Select(ApiKeyResponse.FromEntity));
    }

    [HttpPost]
    public async Task<ActionResult<CreatedApiKeyResponse>> CreateKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.IntegrationsApi, ct)) return ModuleDisabled();

        var generated = ApiKeyGenerator.Generate();
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            KeyPrefix = generated.KeyPrefix,
            KeyHash = generated.Hash,
            CreatedByUserId = User.GetUserId(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.ApiKeys.Add(key);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.ApiKey, key.Id, $"Created API key '{key.Name}'.");

        await _db.SaveChangesAsync(ct);

        // The only point in this key's lifetime the plaintext value is ever
        // available - not stored anywhere, and this response is the caller's
        // only chance to copy it down.
        return CreatedAtAction(nameof(GetKeys),
            new CreatedApiKeyResponse(key.Id, key.Name, key.KeyPrefix, generated.Plaintext, key.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RevokeKey(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.IntegrationsApi, ct)) return ModuleDisabled();

        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null) return NotFound();
        if (key.RevokedAt is not null) return NoContent();

        key.RevokedAt = DateTime.UtcNow;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.ApiKey, key.Id, $"Revoked API key '{key.Name}'.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ObjectResult ModuleDisabled() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Integrations & API isn't enabled for this workspace - contact WMX to turn it on." });
}
