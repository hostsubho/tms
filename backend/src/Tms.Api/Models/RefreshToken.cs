namespace Tms.Api.Models;

// Opaque refresh tokens are stored hashed (never plaintext) so a DB leak
// doesn't hand out usable tokens. Rotated on every use (old one revoked,
// new one issued) to limit the blast radius of a stolen token.
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
