using Tms.Api.Models;

namespace Tms.Api.Dtos.Roles;

public record CreateCustomRoleRequest(string Name, List<Permission> Permissions);

public record UpdateCustomRoleRequest(string? Name, List<Permission>? Permissions);

public record CustomRoleResponse(Guid Id, string Name, List<Permission> Permissions, DateTime CreatedAt)
{
    public static CustomRoleResponse FromEntity(CustomRole role, List<Permission> permissions) =>
        new(role.Id, role.Name, permissions, role.CreatedAt);
}
