using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Utsav.Domain.Authorization;

namespace Utsav.Api.Authentication;

/// <summary>One capability an endpoint needs the caller to have — see <see cref="Permission"/>.</summary>
public sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

/// <summary>
/// Resolves the caller's role from the session claim and checks it against
/// <see cref="PermissionMatrix"/> — the one place that table exists. An
/// unparseable or missing role fails closed (no match, not an exception),
/// the same "unknown means denied, not admin" posture
/// <see cref="AppRoleExtensions.TryParseWire"/> takes at the data layer.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (AppRoleExtensions.TryParseWire(roleClaim, out var role) && PermissionMatrix.Allows(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class PermissionAuthorizationExtensions
{
    /// <summary>
    /// Active membership plus a specific permission — the two checks every
    /// permission-gated route needs, in one call. Mirrors
    /// <c>RequireAuthorization(AuthorizationPolicies.Admin)</c>, just scoped to
    /// what the route actually needs instead of admin-or-nothing.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy
            .RequireClaim(LegacySessionAuthenticationDefaults.StatusClaimType, "active")
            .AddRequirements(new PermissionRequirement(permission)));
}
