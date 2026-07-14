namespace Tms.Api.Models;

// Module 1 - Authentication & Identity (SSO). A short-lived, single-use
// anti-CSRF/anti-replay token for the SP-initiated redirect flow:
// SsoAuthController.Start creates one and hands the plaintext token to the
// browser (as the OIDC "state" param, or the SAML "RelayState" param); the
// callback/ACS endpoint looks it up by hash, checks it hasn't expired or
// already been consumed, and marks it consumed - so a captured/replayed
// callback URL can't be used twice. Same opaque-random-token-hashed-at-rest
// shape as RefreshToken, just minutes-lived instead of weeks-lived.
public class SsoLoginState
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public SsoProtocol Protocol { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
