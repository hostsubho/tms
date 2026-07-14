using Microsoft.AspNetCore.Authorization;
using Tms.Api.Models;

namespace Tms.Api.Services;

// Module 12 - Roles & Permissions. One requirement per Permission enum
// value; a policy is pre-registered for every value in Program.cs at
// startup. The Permission enum is small, fixed in code, and not
// tenant-configurable, so this doesn't need a dynamic
// IAuthorizationPolicyProvider - a simple foreach at startup is enough.
public class PermissionRequirement : IAuthorizationRequirement
{
    public Permission Permission { get; }

    public PermissionRequirement(Permission permission)
    {
        Permission = permission;
    }
}
