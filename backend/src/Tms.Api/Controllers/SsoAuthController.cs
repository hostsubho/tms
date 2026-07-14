using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 1 - Authentication & Identity (SSO). Entirely unauthenticated, like
// AuthController.Login/Register - this IS the login flow, so there is no
// token yet when these actions run. Every action here is reached via a full
// browser navigation (redirect, or the IdP's own form POST to the ACS URL),
// never a fetch/XHR from the frontend SPA - so none of this needs CORS, and
// the "response" to a successful login is itself a redirect back to the
// frontend with tokens in the URL fragment (see RedirectToFrontendWithTokens),
// not a JSON body.
[ApiController]
[Route("api/auth/sso")]
public class SsoAuthController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IJwtTokenService _tokenService;
    private readonly IOidcService _oidc;
    private readonly IConfiguration _config;
    private readonly IModuleAccessService _moduleAccess;

    public SsoAuthController(
        TmsDbContext db,
        ITenantContext tenantContext,
        IJwtTokenService tokenService,
        IOidcService oidc,
        IConfiguration config,
        IModuleAccessService moduleAccess)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tokenService = tokenService;
        _oidc = oidc;
        _config = config;
        _moduleAccess = moduleAccess;
    }

    [HttpGet("{tenantSlug}/start")]
    public async Task<IActionResult> Start(string tenantSlug, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == tenantSlug, ct);
        if (tenant is null)
        {
            return NotFound(new { message = "Unknown tenant." });
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Churned)
        {
            return Forbid();
        }

        // Resolve tenant context before querying SsoConfigs, same pre-auth
        // pattern AuthController.Login uses for Tenants/Users.
        _tenantContext.TenantId = tenant.Id;

        if (!await _moduleAccess.IsEnabledAsync(tenant.Id, ModuleKey.Sso, ct))
        {
            return BadRequest(new { message = "SSO is not enabled for this workspace." });
        }

        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.TenantId == tenant.Id, ct);
        if (config is null || !config.Enabled)
        {
            return BadRequest(new { message = "SSO is not enabled for this workspace." });
        }

        var stateToken = _tokenService.GenerateRefreshToken();
        _db.SsoLoginStates.Add(new SsoLoginState
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Protocol = config.Protocol,
            TokenHash = _tokenService.HashToken(stateToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        });
        await _db.SaveChangesAsync(ct);

        if (config.Protocol == SsoProtocol.Oidc)
        {
            if (string.IsNullOrWhiteSpace(config.OidcAuthority) || string.IsNullOrWhiteSpace(config.OidcClientId))
            {
                return BadRequest(new { message = "OIDC is not fully configured for this workspace." });
            }

            OidcDiscoveryDocument discovery;
            try
            {
                discovery = await _oidc.GetDiscoveryDocumentAsync(config.OidcAuthority, ct);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { message = $"Could not reach the identity provider: {ex.Message}" });
            }

            var authUrl =
                $"{discovery.AuthorizationEndpoint}" +
                $"?client_id={Uri.EscapeDataString(config.OidcClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(BuildOidcRedirectUri())}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString("openid email profile")}" +
                $"&state={Uri.EscapeDataString(stateToken)}";

            return Redirect(authUrl);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.SamlIdpSsoUrl))
            {
                return BadRequest(new { message = "SAML is not fully configured for this workspace." });
            }

            var redirectUrl = SamlHelper.BuildAuthnRequestRedirectUrl(
                config.SamlIdpSsoUrl,
                spEntityId: $"urn:tms:{tenant.Subdomain}",
                acsUrl: BuildSamlAcsUrl(),
                relayState: stateToken);

            return Redirect(redirectUrl);
        }
    }

    [HttpGet("oidc/callback")]
    public async Task<IActionResult> OidcCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return RedirectToFrontendError($"Your identity provider declined the sign-in: {error}");
        }

        var loginState = await ConsumeStateAsync(state, SsoProtocol.Oidc, ct);
        if (loginState is null || string.IsNullOrEmpty(code))
        {
            return RedirectToFrontendError("Your sign-in link expired or was already used. Try signing in again.");
        }

        _tenantContext.TenantId = loginState.TenantId;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == loginState.TenantId, ct);
        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.TenantId == loginState.TenantId, ct);

        if (tenant is null || config is null || !config.Enabled || config.Protocol != SsoProtocol.Oidc
            || string.IsNullOrWhiteSpace(config.OidcAuthority) || string.IsNullOrWhiteSpace(config.OidcClientId) || string.IsNullOrWhiteSpace(config.OidcClientSecret)
            || !await _moduleAccess.IsEnabledAsync(loginState.TenantId, ModuleKey.Sso, ct))
        {
            return RedirectToFrontendError("SSO is no longer available for this workspace.");
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Churned)
        {
            return RedirectToFrontendError("This workspace is not currently active.");
        }

        string email;
        string subject;
        try
        {
            var discovery = await _oidc.GetDiscoveryDocumentAsync(config.OidcAuthority, ct);
            var idToken = await _oidc.ExchangeCodeForIdTokenAsync(
                discovery, config.OidcClientId, config.OidcClientSecret, code, BuildOidcRedirectUri(), ct);
            var principal = await _oidc.ValidateIdTokenAsync(discovery, idToken, config.OidcClientId, ct);

            subject = principal.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("The identity provider's id_token was missing a 'sub' claim.");
            email = principal.FindFirst("email")?.Value
                ?? throw new InvalidOperationException("The identity provider's id_token was missing an 'email' claim.");
        }
        catch (Exception ex)
        {
            return RedirectToFrontendError($"Sign-in with your identity provider failed: {ex.Message}");
        }

        AppUser user;
        try
        {
            user = await ProvisionUserAsync(tenant.Id, email, subject, ct);
        }
        catch (InvalidOperationException ex)
        {
            return RedirectToFrontendError(ex.Message);
        }

        var tokens = await IssueTokensAsync(user, ct);
        return RedirectToFrontendWithTokens(tenant.Subdomain, tokens);
    }

    [HttpPost("saml/acs")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> SamlAcs([FromForm] string? SAMLResponse, [FromForm] string? RelayState, CancellationToken ct)
    {
        var loginState = await ConsumeStateAsync(RelayState, SsoProtocol.Saml, ct);
        if (loginState is null || string.IsNullOrEmpty(SAMLResponse))
        {
            return RedirectToFrontendError("Your sign-in link expired or was already used. Try signing in again.");
        }

        _tenantContext.TenantId = loginState.TenantId;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == loginState.TenantId, ct);
        var config = await _db.SsoConfigs.FirstOrDefaultAsync(c => c.TenantId == loginState.TenantId, ct);

        if (tenant is null || config is null || !config.Enabled || config.Protocol != SsoProtocol.Saml
            || string.IsNullOrWhiteSpace(config.SamlIdpCertificate)
            || !await _moduleAccess.IsEnabledAsync(loginState.TenantId, ModuleKey.Sso, ct))
        {
            return RedirectToFrontendError("SSO is no longer available for this workspace.");
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Churned)
        {
            return RedirectToFrontendError("This workspace is not currently active.");
        }

        string email;
        try
        {
            var verified = SamlHelper.ParseAndVerifyResponse(SAMLResponse, config.SamlIdpCertificate);
            email = verified.NameId;
        }
        catch (Exception ex)
        {
            return RedirectToFrontendError($"Sign-in with your identity provider failed: {ex.Message}");
        }

        AppUser user;
        try
        {
            // SAML has no separate "subject" distinct from NameID in this
            // baseline implementation - NameID doubles as both the login
            // identity and the SsoSubjectId backfill value.
            user = await ProvisionUserAsync(tenant.Id, email, subject: email, ct);
        }
        catch (InvalidOperationException ex)
        {
            return RedirectToFrontendError(ex.Message);
        }

        var tokens = await IssueTokensAsync(user, ct);
        return RedirectToFrontendWithTokens(tenant.Subdomain, tokens);
    }

    private async Task<SsoLoginState?> ConsumeStateAsync(string? plaintextState, SsoProtocol expectedProtocol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plaintextState)) return null;

        var hash = _tokenService.HashToken(plaintextState);

        // Looked up by hash across all tenants (tenant isn't known from the
        // URL on this callback) - same IgnoreQueryFilters() pre-tenant-
        // context pattern as AuthController.Refresh's RefreshToken lookup.
        var state = await _db.SsoLoginStates.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (state is null || state.ConsumedAt is not null || state.ExpiresAt < DateTime.UtcNow || state.Protocol != expectedProtocol)
        {
            return null;
        }

        state.ConsumedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return state;
    }

    // JIT provisioning, matched by (TenantId, Email) - the same identity key
    // every local-password AppUser already uses (see the unique index in
    // TmsDbContext). An existing local account is "claimed" by SSO the first
    // time its owner signs in that way (SsoSubjectId backfilled onto it); a
    // first-time email creates a brand-new Agent-role account, the same
    // default AuthController.Register gives every non-first user in a
    // tenant - an admin can promote them afterward via UsersController, same
    // as any other newly invited user.
    private async Task<AppUser> ProvisionUserAsync(Guid tenantId, string email, string subject, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email,
                Role = Role.Agent,
                SsoSubjectId = subject,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Users.Add(user);
        }
        else
        {
            if (!user.IsActive)
            {
                throw new InvalidOperationException("This account has been deactivated. Contact your workspace admin.");
            }

            if (user.SsoSubjectId is null)
            {
                user.SsoSubjectId = subject;
            }
        }

        await _db.SaveChangesAsync(ct);
        return user;
    }

    // Mirrors AuthController.IssueTokensAsync - duplicated deliberately
    // rather than factored into a shared helper, matching this codebase's
    // existing convention of each auth surface (staff/platform/portal) owning
    // its own token issuance end-to-end (see PlatformAuthController,
    // PortalAuthController).
    private async Task<SsoIssuedTokens> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var permissions = user.CustomRoleId is null
            ? Array.Empty<Permission>()
            : await _db.CustomRolePermissions
                .Where(p => p.CustomRoleId == user.CustomRoleId)
                .Select(p => p.Permission)
                .ToArrayAsync(ct);

        var accessToken = _tokenService.CreateAccessToken(user, permissions);
        var refreshTokenPlaintext = _tokenService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(refreshTokenPlaintext),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync(ct);

        return new SsoIssuedTokens(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshTokenPlaintext,
            user.Id,
            user.Email,
            user.Role.ToString(),
            permissions.Select(p => p.ToString()).ToArray());
    }

    private record SsoIssuedTokens(
        string AccessToken,
        DateTime AccessTokenExpiresAtUtc,
        string RefreshToken,
        Guid UserId,
        string Email,
        string Role,
        string[] Permissions);

    private IActionResult RedirectToFrontendWithTokens(string tenantSlug, SsoIssuedTokens tokens)
    {
        // URL fragment (#), not a query string (?) - fragments are never
        // sent to the server or captured in access logs/Referer headers,
        // only readable by the frontend's own JS once loaded on
        // /sso/callback. Same reasoning OAuth2's old implicit-flow redirects
        // put tokens in the fragment for. Field names/shape here match the
        // frontend's TenantAuth type exactly (see lib/auth.ts) - /sso/callback
        // just reassembles this fragment straight into that shape, the same
        // way login/page.tsx builds it from POST /api/auth/login's response
        // plus the tenantSlug it already had client-side (which the SSO
        // redirect flow never has, hence sending it here instead).
        var fragment =
            $"access_token={Uri.EscapeDataString(tokens.AccessToken)}" +
            $"&refresh_token={Uri.EscapeDataString(tokens.RefreshToken)}" +
            $"&expires_at={Uri.EscapeDataString(tokens.AccessTokenExpiresAtUtc.ToString("o"))}" +
            $"&user_id={Uri.EscapeDataString(tokens.UserId.ToString())}" +
            $"&email={Uri.EscapeDataString(tokens.Email)}" +
            $"&role={Uri.EscapeDataString(tokens.Role)}" +
            $"&tenant_slug={Uri.EscapeDataString(tenantSlug)}" +
            $"&permissions={Uri.EscapeDataString(string.Join(',', tokens.Permissions))}";

        return Redirect($"{FrontendBaseUrl}/sso/callback#{fragment}");
    }

    private IActionResult RedirectToFrontendError(string message) =>
        Redirect($"{FrontendBaseUrl}/sso/callback#error={Uri.EscapeDataString(message)}");

    private string FrontendBaseUrl => (_config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    // Fixed, tenant-independent URLs (the tenant is recovered from the
    // "state"/"RelayState" value, not the URL) - see SsoConfigController's
    // matching computation, which is what a tenant admin pastes into their
    // IdP's app registration.
    private string BackendBaseUrl => (_config["Auth:BackendBaseUrl"] ?? "https://tms-1tv2.onrender.com").TrimEnd('/');
    private string BuildOidcRedirectUri() => $"{BackendBaseUrl}/api/auth/sso/oidc/callback";
    private string BuildSamlAcsUrl() => $"{BackendBaseUrl}/api/auth/sso/saml/acs";
}
