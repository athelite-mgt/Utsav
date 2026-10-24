using Utsav.Api.Authentication;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Operating the platform, as distinct from operating a committee. Everything
/// here sits behind <see cref="AuthorizationPolicies.PlatformAdmin"/>, which no
/// committee role can satisfy: a committee admin is the most powerful committee
/// role and still has no platform authority.
///
/// Holding the capability grants no access to any committee's data. These
/// routes manage committees; reading a committee's books still takes an
/// ordinary membership in it, and every tenancy layer applies unchanged.
/// </summary>
public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/platform").RequireAuthorization(AuthorizationPolicies.PlatformAdmin);

        // Lets the client decide whether to offer the platform console at all.
        platform.MapGet("/whoami", () => Results.Ok(new { platform_admin = true }));

        return app;
    }
}
