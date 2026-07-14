using Tms.Api.Models;

namespace Tms.Api.Services;

public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    /// Module 12 - Roles & Permissions. `permissions` is the caller's
    /// resolved set of grants from the user's assigned CustomRole (empty/
    /// null if they have none) - JwtTokenService itself has no database
    /// access (it's a stateless Singleton), so callers (AuthController)
    /// resolve this before calling in.
    ///
    /// Module 5.1 - Tenant impersonation. `impersonatorEmail`, when set,
    /// adds an "imp" claim carrying the Super Admin's own email - the
    /// resulting token otherwise looks and behaves exactly like a normal
    /// AppUser token (same claims, same policies satisfied), which is the
    /// point: impersonation should exercise the exact same authorization
    /// paths a real login would, not a parallel "impersonation mode." See
    /// ClaimsPrincipalExtensions.GetEmail(), which surfaces this claim so
    /// every existing audit-log call site across the app automatically
    /// attributes impersonated actions to the real actor, with zero changes
    /// needed at each call site.
    AccessTokenResult CreateAccessToken(AppUser user, IReadOnlyCollection<Permission>? permissions = null, string? impersonatorEmail = null);

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
