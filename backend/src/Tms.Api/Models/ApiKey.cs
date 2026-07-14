namespace Tms.Api.Models;

// Module 11 - Integrations & Public API. A tenant-scoped credential granting
// access to the versioned public REST API (see PublicTicketsController) via
// the X-Api-Key header, authenticated by ApiKeyAuthenticationHandler.
// Modeled after RefreshToken - only a SHA-256 hash is ever persisted, never
// the plaintext key, which is shown to the caller exactly once at creation
// time (see ApiKeysController.CreateKey) and can never be retrieved again.
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;

    // First few characters of the plaintext key, stored unhashed purely so
    // the management UI can show which key is which ("tms_a1b2...") without
    // ever re-exposing the full secret.
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    // Soft-revoke, not a hard delete - keeps CreatedAt/LastUsedAt around for
    // an admin reviewing what a key did and when it was turned off, same
    // rationale as AuditLog being append-only.
    public DateTime? RevokedAt { get; set; }
}
