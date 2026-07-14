using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tms.Api.Data;

namespace Tms.Api.Services;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

// Module 11 - Integrations & Public API. A separate authentication scheme
// from the default JwtBearer one, registered alongside it (not replacing
// it) and wired to its own "PublicApi" authorization policy - see
// Program.cs.
//
// IMPORTANT: "ApiKey" is a non-default scheme, so ASP.NET Core only invokes
// this handler on demand - inside the authorization middleware, once a
// policy requiring it is evaluated. That runs AFTER TenantResolutionMiddleware
// in the pipeline (UseAuthentication → TenantResolutionMiddleware →
// UseAuthorization). TenantResolutionMiddleware reads context.User's
// "tenant_id" claim assuming authentication has already populated it, which
// is only true for the default (JwtBearer) scheme - for a request
// authenticating via this handler, TenantResolutionMiddleware would run
// first, see an anonymous principal, and leave ITenantContext.TenantId
// null, breaking every tenant-scoped query filter and every
// `_tenantContext.TenantId ?? throw` guard in the public API controller.
// Rather than reordering the shared middleware pipeline (which every
// staff/portal request also depends on), this handler sets
// ITenantContext.TenantId directly, itself, the moment it resolves the key
// - sidestepping the ordering problem entirely for this one scheme.
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private const string HeaderName = "X-Api-Key";
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TmsDbContext db,
        ITenantContext tenantContext)
        : base(options, logger, encoder)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            return AuthenticateResult.NoResult();
        }

        var plaintextKey = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return AuthenticateResult.Fail("Missing API key.");
        }

        var hash = ApiKeyGenerator.Hash(plaintextKey);

        // Tenant isn't known yet at this point in the pipeline - authentication
        // has to run before TenantResolutionMiddleware can set it - so this
        // lookup must bypass the DbContext's own TenantId query filter, same
        // reasoning AuthController uses to resolve a tenant by subdomain at
        // login.
        var apiKey = await _db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == hash);

        if (apiKey is null || apiKey.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        // A deliberate exception to this codebase's "one SaveChangesAsync per
        // controller action" convention - authentication happens once, here,
        // before any controller action runs, so there's no later save this
        // could piggyback on. The same DbContext instance (scoped per
        // request) is reused by whichever controller action runs next, and
        // separate SaveChangesAsync calls against it are safe.
        apiKey.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Set directly rather than relying on TenantResolutionMiddleware to
        // pick it up from the "tenant_id" claim below - see the class
        // comment for why that middleware can't be trusted to run after
        // this handler for a non-default scheme.
        _tenantContext.TenantId = apiKey.TenantId;

        var claims = new[]
        {
            new Claim("tenant_id", apiKey.TenantId.ToString()),
            new Claim("scope", "public_api"),
            new Claim("api_key_id", apiKey.Id.ToString()),
            new Claim("api_key_name", apiKey.Name),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
