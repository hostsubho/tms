using Tms.Api.Models;

namespace Tms.Api.Dtos.Users;

public record UserResponse(Guid Id, string Email, Role Role, bool IsActive, Guid? CustomRoleId, string? CustomRoleName)
{
    public static UserResponse FromEntity(AppUser u, string? customRoleName) =>
        new(u.Id, u.Email, u.Role, u.IsActive, u.CustomRoleId, customRoleName);
}

// Module 12 - Roles & Permissions. `CustomRoleId: null` unassigns - a user
// reverts to whatever their built-in Role alone already grants.
public record AssignCustomRoleRequest(Guid? CustomRoleId);
