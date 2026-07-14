using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Sso;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 1 - Authentication & Identity (SSO). Admin-only, same reasoning as
// ApiKeysController/CustomRolesController: this config holds (or gates) live
// credentials that grant sign-in access to the whole tenant workspace - at
// least as sensitive as an API key, not something a Manager should touch.
[ApiController]
[Route("api/tenant/sso")]
[Authorize(Policy = "TenantStaff")]
[Authorize(Roles = "Admin")]
public class SsoConfigController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;
    private readonly IConfiguration _config;

    public SsoConfigController(
        TmsDbContext db,
        ITenantContext tenantContext,
        IAuditLogService auditLog,
        IConfiguration config)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
        _config = config;
    }

    [HttpGet]
    public async Task<ActionResult<TenantSsoConfigResponse>> GetConfig(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        if (config is null)
        {
            // No config yet - report a stable, empty shape (protocol
            // defaults to Oidc purely as the form's starting selection, not
            // a stored value) so the settings page always has something to
            // render rather than branching on 404.
            return Ok(new TenantSsoConfigResponse(
                IsConfigured: false,
                Protocol: nameof(SsoProtocol.Oidc),
                Enabled: false,
                OidcAuthority: null,
                OidcClientId: null,
                HasOidcClientSecret: false,
                SamlIdpEntityId: null,
                SamlIdpSsoUrl: null,
                HasSamlIdpCertificate: false,
                SpEntityId: BuildSpEntityId(tenant.Subdomain),
                OidcRedirectUri: BuildOidcRedirectUri(),
                SamlAcsUrl: BuildSamlAcsUrl()));
        }

        return Ok(ToResponse(config, tenant.Subdomain));
    }

    [HttpPut]
    public async Task<ActionResult<TenantSsoConfigResponse>> UpsertConfig([FromBody] UpsertSsoConfigRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<SsoProtocol>(request.Protocol, ignoreCase: true, out var protocol))
        {
            return BadRequest(new { message = $"Unknown protocol '{request.Protocol}'. Must be 'Oidc' or 'Saml'." });
        }

        if (protocol == SsoProtocol.Oidc && (string.IsNullOrWhiteSpace(request.OidcAuthority) || string.IsNullOrWhiteSpace(request.OidcClientId)))
        {
            return BadRequest(new { message = "OidcAuthority and OidcClientId are required for the OIDC protocol." });
        }

        if (protocol == SsoProtocol.Saml && (string.IsNullOrWhiteSpace(request.SamlIdpEntityId) || string.IsNullOrWhiteSpace(request.SamlIdpSsoUrl)))
        {
            return BadRequest(new { message = "SamlIdpEntityId and SamlIdpSsoUrl are required for the SAML protocol." });
        }

        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        var isNew = config is null;
        if (config is null)
        {
            config = new TenantSsoConfig
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.SsoConfigs.Add(config);
        }

        config.Protocol = protocol;
        config.Enabled = request.Enabled;
        config.OidcAuthority = request.OidcAuthority;
        config.OidcClientId = request.OidcClientId;
        // Blank means "leave whatever's already stored" - see the DTO's own
        // doc comment. A tenant admin editing the authority/client id
        // shouldn't be forced to re-paste the secret/certificate every time.
        if (!string.IsNullOrWhiteSpace(request.OidcClientSecret))
        {
            config.OidcClientSecret = request.OidcClientSecret;
        }
        config.SamlIdpEntityId = request.SamlIdpEntityId;
        config.SamlIdpSsoUrl = request.SamlIdpSsoUrl;
        if (!string.IsNullOrWhiteSpace(request.SamlIdpCertificate))
        {
            config.SamlIdpCertificate = request.SamlIdpCertificate;
        }
        config.UpdatedAt = DateTime.UtcNow;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(),
            isNew ? AuditAction.Created : AuditAction.Updated,
            AuditEntityType.SsoConfig, config.Id,
            $"{(isNew ? "Configured" : "Updated")} SSO ({protocol}), enabled={request.Enabled}.");

        await _db.SaveChangesAsync(ct);

        return Ok(ToResponse(config, tenant.Subdomain));
    }

    private TenantSsoConfigResponse ToResponse(TenantSsoConfig config, string tenantSubdomain) => new(
        IsConfigured: true,
        Protocol: config.Protocol.ToString(),
        Enabled: config.Enabled,
        OidcAuthority: config.OidcAuthority,
        OidcClientId: config.OidcClientId,
        HasOidcClientSecret: !string.IsNullOrEmpty(config.OidcClientSecret),
        SamlIdpEntityId: config.SamlIdpEntityId,
        SamlIdpSsoUrl: config.SamlIdpSsoUrl,
        HasSamlIdpCertificate: !string.IsNullOrEmpty(config.SamlIdpCertificate),
        SpEntityId: BuildSpEntityId(tenantSubdomain),
        OidcRedirectUri: BuildOidcRedirectUri(),
        SamlAcsUrl: BuildSamlAcsUrl());

    // A single fixed redirect URI/ACS URL for the whole deployment (not
    // per-tenant) - the tenant is recovered from the "state"/"RelayState"
    // value on callback, not from the URL, so every tenant's IdP app
    // registration points at the same two URLs. See SsoAuthController.
    private string BackendBaseUrl => (_config["Auth:BackendBaseUrl"] ?? "https://tms-1tv2.onrender.com").TrimEnd('/');
    private string BuildOidcRedirectUri() => $"{BackendBaseUrl}/api/auth/sso/oidc/callback";
    private string BuildSamlAcsUrl() => $"{BackendBaseUrl}/api/auth/sso/saml/acs";
    private static string BuildSpEntityId(string tenantSubdomain) => $"urn:tms:{tenantSubdomain}";
}
