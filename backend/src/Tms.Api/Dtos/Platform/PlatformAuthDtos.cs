namespace Tms.Api.Dtos.Platform;

// Bootstrap only works once (see PlatformAuthController.Bootstrap) - it
// creates the very first PlatformUser as Owner. Every PlatformUser after
// that has to be created by an existing Owner/PlatformAdmin (not built yet -
// track as a follow-up before this goes anywhere near production, since
// right now there's no in-app way to add a second platform admin).
public record BootstrapRequest(string Name, string Email, string Password);

public record PlatformLoginRequest(string Email, string Password);

public record PlatformAuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid UserId,
    string Email,
    string Role);
