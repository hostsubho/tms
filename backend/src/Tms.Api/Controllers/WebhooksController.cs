using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Webhooks;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 11 - Integrations & Public API. Admin-only, same reasoning as
// ApiKeysController - a webhook subscription is itself a standing form of
// automatic outbound data egress, sensitive enough to gate the same way.
// Gated behind the IntegrationsApi module flag ("Module Licensing" - see
// IModuleAccessService), shared with ApiKeysController since both live under
// the same "Integrations" nav entry/tenant-facing feature area.
[ApiController]
[Route("api/webhooks")]
[Authorize(Policy = "TenantStaff")]
[Authorize(Roles = "Admin")]
public class WebhooksController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;
    private readonly IModuleAccessService _moduleAccess;

    public WebhooksController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog, IModuleAccessService moduleAccess)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
        _moduleAccess = moduleAccess;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WebhookResponse>>> GetWebhooks(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.IntegrationsApi, ct)) return ModuleDisabled();

        var webhooks = await _db.WebhookSubscriptions.OrderByDescending(w => w.CreatedAt).ToListAsync(ct);
        return Ok(webhooks.Select(WebhookResponse.FromEntity));
    }

    [HttpPost]
    public async Task<ActionResult<CreatedWebhookResponse>> CreateWebhook([FromBody] CreateWebhookRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.IntegrationsApi, ct)) return ModuleDisabled();

        var validationError = await WebhookUrlValidator.ValidateAsync(request.Url);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var secretBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToHexString(secretBytes).ToLowerInvariant();

        var webhook = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Url = request.Url,
            Event = request.Event,
            Secret = secret,
            IsActive = true,
            CreatedByUserId = User.GetUserId(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.WebhookSubscriptions.Add(webhook);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.WebhookSubscription, webhook.Id, $"Created webhook for {webhook.Event} → {webhook.Url}.");

        await _db.SaveChangesAsync(ct);

        // Secret is returned exactly once, here - it's stored in reversible
        // form server-side (see WebhookSubscription.Secret) since delivery
        // needs to reuse it on every request, but this response is still the
        // only time the admin who created it gets to see it directly.
        return CreatedAtAction(nameof(GetWebhooks),
            new CreatedWebhookResponse(webhook.Id, webhook.Url, webhook.Event, secret, webhook.CreatedAt));
    }

    // Deliberately NOT gated by IsEnabledAsync (unlike GetWebhooks/CreateWebhook/
    // GetLogs above) - this only pauses/resumes an existing subscription, and
    // delivery itself was never gated by the module flag to begin with
    // (WebhookService fires regardless), so blocking pause here would remove
    // exactly the "turn this off" lever an Admin needs most right when the
    // module is disabled. Same safety-action carve-out as ApiKeysController.RevokeKey.
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<WebhookResponse>> UpdateWebhook(Guid id, [FromBody] UpdateWebhookRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var webhook = await _db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null) return NotFound();

        // Deliberately minimal - only pause/resume. Changing the URL or
        // event is a delete-and-recreate (same convention SlaPolicy uses for
        // its own immutable-after-creation fields), which also forces a
        // fresh secret rather than silently repointing an existing one.
        if (request.IsActive is not null) webhook.IsActive = request.IsActive.Value;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.WebhookSubscription, webhook.Id,
            $"{(webhook.IsActive ? "Resumed" : "Paused")} webhook for {webhook.Event}.");

        await _db.SaveChangesAsync(ct);
        return Ok(WebhookResponse.FromEntity(webhook));
    }

    // Deliberately NOT gated, same reasoning as UpdateWebhook above - deleting
    // is the other "turn this off" safety lever and shouldn't lock up behind
    // the very module flag an Admin might have just switched off.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWebhook(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var webhook = await _db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null) return NotFound();

        // Delivery logs stay behind with a now-dangling WebhookSubscriptionId
        // - same convention as AutomationRuleLog surviving its parent rule's
        // deletion.
        _db.WebhookSubscriptions.Remove(webhook);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.WebhookSubscription, webhook.Id, $"Deleted webhook for {webhook.Event} → {webhook.Url}.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/logs")]
    public async Task<ActionResult<IEnumerable<WebhookDeliveryLogResponse>>> GetLogs(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.IntegrationsApi, ct)) return ModuleDisabled();

        var logs = await _db.WebhookDeliveryLogs
            .Where(l => l.WebhookSubscriptionId == id)
            .OrderByDescending(l => l.AttemptedAt)
            .Take(50)
            .ToListAsync(ct);

        return Ok(logs.Select(WebhookDeliveryLogResponse.FromEntity));
    }

    private ObjectResult ModuleDisabled() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Integrations & API isn't enabled for this workspace - contact WMX to turn it on." });
}
