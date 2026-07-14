using Microsoft.AspNetCore.Authorization;
using Tms.Api.Models;

namespace Tms.Api.Services;

// Module 12 - Roles & Permissions. Admin and Manager - the two built-in
// roles that already had blanket access to every "Admin/Manager only"
// endpoint before this module existed - keep that access unconditionally.
// Replacing `[Authorize(Roles = "Admin,Manager")]` with
// `[Authorize(Policy = "Permission:X")]` on those actions is therefore a
// pure superset: zero behavior change for any existing tenant, plus the
// new ability to grant one *specific* module's permission to an
// Agent/ReadOnly user via a custom role, without promoting them all the
// way to Manager.
//
// Permissions are read from a "permissions" JWT claim snapshotted at login
// (see JwtTokenService/AuthController), not looked up from the database
// here - the same tradeoff already accepted for the Role claim itself: a
// permission change (or a custom-role reassignment) takes effect on that
// user's next login/refresh, not instantly. Adding a database round trip
// to every authorization check, on every request to every gated endpoint,
// isn't worth it to remove a staleness window that already exists
// elsewhere in this same token.
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.IsInRole(nameof(Role.Admin)) || context.User.IsInRole(nameof(Role.Manager)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var granted = context.User.FindFirst("permissions")?.Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        if (granted.Contains(requirement.Permission.ToString()))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
