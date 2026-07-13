using Tms.Api.Models;

namespace Tms.Api.Services;

public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(AppUser user);

    /// Returns the plaintext refresh token (given to the client) - callers
    /// are responsible for persisting only its hash via HashToken().
    string GenerateRefreshToken();

    string HashToken(string plaintextToken);
}
