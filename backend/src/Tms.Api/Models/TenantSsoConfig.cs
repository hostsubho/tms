namespace Tms.Api.Models;

// Module 1 - Authentication & Identity (SSO). A tenant configures exactly one
// protocol at a time (matches the spec's "SAML 2.0 and OIDC ... configurable
// per tenant" - not both simultaneously for the same tenant), enforced by the
// unique TenantId index in TmsDbContext rather than a second table per
// protocol.
public enum SsoProtocol
{
    Oidc,
    Saml,
}

// Admin-only CRUD via SsoConfigController; read by the unauthenticated
// SsoAuthController.Start/callback endpoints before any user is signed in
// (same "resolve tenant first, then query" pattern AuthController.Login uses
// for Tenants/Users).
//
// SECURITY NOTE: OidcClientSecret is a genuine reusable secret (sent back to
// the IdP's token endpoint on every login), not a one-way bearer token like
// ApiKey.KeyHash or RefreshToken.TokenHash - so it cannot be stored as a hash
// and must be stored reversibly. This codebase has no existing
// secret-at-rest encryption mechanism (Stripe's secret key lives in
// IConfiguration/env vars, never the DB), so for now this rides on Neon's
// at-rest encryption only, same as SamlIdpCertificate below. Encrypting this
// column (e.g. via the ASP.NET Core Data Protection API) before storing is
// flagged as follow-up hardening for the "Enterprise hardening" phase, not
// done here - never returned by any API response (see
// TenantSsoConfigResponse.HasOidcClientSecret).
public class TenantSsoConfig
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public SsoProtocol Protocol { get; set; }
    public bool Enabled { get; set; }

    // OIDC fields. The discovery document is fetched fresh from
    // "{OidcAuthority}/.well-known/openid-configuration" at the start of
    // every login rather than cached, so an IdP-side endpoint rotation or
    // key rollover takes effect on the next login with no redeploy here.
    public string? OidcAuthority { get; set; }
    public string? OidcClientId { get; set; }
    public string? OidcClientSecret { get; set; }

    // SAML fields. SamlIdpCertificate is the IdP's public signing
    // certificate (PEM or raw base64 DER) used to verify the signature on
    // every incoming SAMLResponse - it is public key material, not a secret,
    // same reasoning as it being safe to publish in real SAML metadata.
    public string? SamlIdpEntityId { get; set; }
    public string? SamlIdpSsoUrl { get; set; }
    public string? SamlIdpCertificate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
