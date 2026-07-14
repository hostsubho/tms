namespace Tms.Api.Dtos.Sso;

// Module 1 - Authentication & Identity (SSO). GET always returns 200 with
// IsConfigured=false rather than 404 when no row exists yet - the tenant
// settings page needs a stable shape to render an empty form either way.
// Never echoes OidcClientSecret/SamlIdpCertificate back - same "write-only
// secret" convention CreateApiKeyRequest/ApiKeyResponse already use for API
// keys - callers only learn whether one is set (HasOidcClientSecret /
// HasSamlIdpCertificate), and can tell PUT to leave it alone by sending null.
public record TenantSsoConfigResponse(
    bool IsConfigured,
    string Protocol,
    bool Enabled,
    string? OidcAuthority,
    string? OidcClientId,
    bool HasOidcClientSecret,
    string? SamlIdpEntityId,
    string? SamlIdpSsoUrl,
    bool HasSamlIdpCertificate,
    // Values the tenant admin pastes into their IdP's app registration -
    // computed from this deployment's own base URL + the tenant's
    // subdomain, not stored, so they always reflect the current environment.
    string SpEntityId,
    string OidcRedirectUri,
    string SamlAcsUrl);

// Protocol is a string here (not the SsoProtocol enum) purely so an invalid
// value comes back as a normal 400 validation message from the controller
// rather than an ASP.NET model-binding 400 with a less friendly body.
// OidcClientSecret / SamlIdpCertificate: leave null/empty to keep whatever is
// already stored (relevant on every update after the first) - only a
// non-empty value overwrites it.
public record UpsertSsoConfigRequest(
    string Protocol,
    bool Enabled,
    string? OidcAuthority,
    string? OidcClientId,
    string? OidcClientSecret,
    string? SamlIdpEntityId,
    string? SamlIdpSsoUrl,
    string? SamlIdpCertificate);
