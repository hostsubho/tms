using Tms.Api.Models;

namespace Tms.Api.Services;

public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(AppUser user);

    /// Platform (Super Admin) tokens carry "scope=platform_admin" (matching the
    /// PlatformAdmin authorization policy in Program.cs) plus a platform_role
    /// claim for finer-grained checks - never a tenant_id claim, so a platform
    /// token can never satisfy tenant-scoped endpoints and vice versa.
    AccessTokenResult CreatePlatformAccessToken(PlatformUser user);

    /// Portal customer (Module 7) tokens carry "scope=portal_customer" plus
    /// tenant_id (so TenantResolutionMiddleware and the DbContext query
    /// filters work unchanged) and customer_id. No Role claim, so this token
    /// can never satisfy [Authorize(Roles = ...)] staff endpoints, and it has
    /// no scope=platform_admin either - three mutually exclusive claim sets
    /// for three mutually exclusive auth surfaces, same signing key.
    AccessTokenResult CreatePortalCustomerAccessToken(PortalCustomer customer);

    /// Returns the plaintext refresh token (given to the client) - callers
    /// are responsible for persisting only its hash via HashToken().
    string GenerateRefreshToken();

    string HashToken(string plaintextToken);
}
