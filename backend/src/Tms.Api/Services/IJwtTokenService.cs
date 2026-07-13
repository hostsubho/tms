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

    /// Returns the plaintext refresh token (given to the client) - callers
    /// are responsible for persisting only its hash via HashToken().
    string GenerateRefreshToken();

    string HashToken(string plaintextToken);
}
