namespace Tms.Api.Dtos.Platform;

// Module 5.1 - Tenant impersonation. UserId is optional - omit it to
// impersonate the tenant's own earliest-created Admin (the common "debug an
// issue for this customer" case), or pass a specific user's id to
// impersonate someone else on that tenant (e.g. reproducing a bug reported
// by a particular Agent).
public record ImpersonateRequest(Guid? UserId);

public record ImpersonateResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    Guid UserId,
    string Email,
    string Role,
    List<string> Permissions);

public record ImpersonationLogResponse(
    Guid Id,
    string PlatformUserEmail,
    Guid TenantId,
    string TenantName,
    string TargetUserEmail,
    DateTime StartedAt);
