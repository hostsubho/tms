using System.Security.Cryptography;

namespace Tms.Api.Services;

// Module 11 - Integrations & Public API. Mirrors the hashing approach
// JwtTokenService already uses for refresh tokens (SHA-256, base64) - no
// need for a slower password-hashing algorithm here since the plaintext key
// itself already carries 256 bits of randomness, not a human-chosen secret.
public static class ApiKeyGenerator
{
    private const string KeyPrefixLiteral = "tms_";

    public record GeneratedKey(string Plaintext, string KeyPrefix, string Hash);

    public static GeneratedKey Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        var plaintext = $"{KeyPrefixLiteral}{token}";
        var keyPrefix = plaintext[..Math.Min(12, plaintext.Length)];
        return new GeneratedKey(plaintext, keyPrefix, Hash(plaintext));
    }

    public static string Hash(string plaintextKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintextKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
