using Tms.Api.Models;

namespace Tms.Api.Dtos.Platform;

// Bootstrap only works once (see PlatformAuthController.Bootstrap) - it
// creates the very first PlatformUser as Owner. Every PlatformUser after
// that is created by an existing Owner via PlatformAuthController.AddPlatformUser
// (Module 5.6).
public record BootstrapRequest(string Name, string Email, string Password);

public record PlatformLoginRequest(string Email, string Password);

public record PlatformAuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid UserId,
    string Email,
    string Role);

// Module 5.6 - Owner-only, adds a platform user of any role (including a
// second Owner). See PlatformAuthController.AddPlatformUser.
public record AddPlatformUserRequest(string Name, string Email, string Password, PlatformRole Role);

public record PlatformUserResponse(
    Guid Id,
    string Name,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAt);
