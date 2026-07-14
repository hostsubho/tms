namespace Tms.Api.Dtos.ApiKeys;

public record CreateApiKeyRequest(string Name);

public record ApiKeyResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt)
{
    public static ApiKeyResponse FromEntity(Tms.Api.Models.ApiKey key) => new(
        key.Id, key.Name, key.KeyPrefix, key.CreatedAt, key.LastUsedAt, key.RevokedAt);
}

// Returned exactly once, at creation - the plaintext key is never
// retrievable again after this response (see ApiKeysController.CreateKey).
public record CreatedApiKeyResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    string PlaintextKey,
    DateTime CreatedAt);
