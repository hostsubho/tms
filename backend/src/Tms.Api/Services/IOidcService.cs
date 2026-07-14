using System.Security.Claims;

namespace Tms.Api.Services;

// Module 1 - Authentication & Identity (SSO). The three steps of an OIDC
// Authorization Code flow that need a live HTTP call out to the tenant's IdP
// - kept as a separate interface (rather than inline in SsoAuthController)
// so the controller stays about orchestration/JIT-provisioning, matching the
// existing WebhookService/IWebhookService split for the same reason.
public record OidcDiscoveryDocument(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string JwksUri);

public interface IOidcService
{
    // Fetched fresh on every login rather than cached - see
    // TenantSsoConfig.OidcAuthority's doc comment for why.
    Task<OidcDiscoveryDocument> GetDiscoveryDocumentAsync(string authority, CancellationToken ct);

    Task<string> ExchangeCodeForIdTokenAsync(
        OidcDiscoveryDocument discovery,
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        CancellationToken ct);

    // Verifies the id_token's signature against the IdP's live JWKS (fetched
    // from discovery.JwksUri, not cached/pinned) plus issuer/audience/
    // expiry - returns the validated claims (sub, email) on success, throws
    // on any failure. A failure here must never be treated as "no SSO
    // identity" and silently fall through to another auth path - the caller
    // propagates the exception as a hard login failure.
    Task<ClaimsPrincipal> ValidateIdTokenAsync(
        OidcDiscoveryDocument discovery,
        string idToken,
        string clientId,
        CancellationToken ct);
}
