using Tms.Api.Models;

namespace Tms.Api.Dtos.Users;

public record UserResponse(Guid Id, string Email, Role Role, bool IsActive)
{
    public static UserResponse FromEntity(AppUser u) => new(u.Id, u.Email, u.Role, u.IsActive);
}
