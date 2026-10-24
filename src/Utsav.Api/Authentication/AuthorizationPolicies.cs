using System.Security.Claims;
using Utsav.Domain.Authorization;

namespace Utsav.Api.Authentication;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Ported from <c>requireOrg</c> in <c>server/auth.mjs</c>: authenticated
    /// is not enough — membership in the one committee must also be
    /// 'active', not 'pending' or absent. The baseline every route needs;
    /// most also add a specific <see cref="Permission"/> on top via
    /// <c>RequirePermission</c>.
    /// </summary>
    public const string ActiveMember = "ActiveMember";

    /// <summary>
    /// A platform operator. Needs no membership of any committee, and no
    /// committee role satisfies it: being the admin of a committee grants
    /// nothing here.
    /// </summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>
    /// True admin only — <see cref="Permission.ManageSettings"/> is
    /// Admin-exclusive in <see cref="PermissionMatrix"/>, so this is
    /// equivalent to (and mostly superseded by)
    /// <c>RequirePermission(Permission.ManageSettings)</c>. Kept for the
    /// handful of call sites that want to say "admin" directly rather than
    /// reach for a settings-shaped permission that doesn't quite name what
    /// they mean.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Read-only report export, reachable either by a signed-in member with
    /// <see cref="Permission.ReadAudit"/> or by a machine presenting a valid
    /// API key (see <c>ApiKeyAuthenticationHandler</c>). The only policy a
    /// key can satisfy — see Program.cs for how it's composed and why it is
    /// kept separate from <see cref="ActiveMember"/>.
    /// </summary>
    public const string ReportExport = "ReportExport";

    /// <summary>
    /// The caller's role for this request, resolved the same way
    /// <see cref="PermissionAuthorizationHandler"/> does — parsed from the
    /// session claim, failing closed (null, not a guess) if it's missing or
    /// unrecognised.
    /// </summary>
    public static AppRole? CallerRole(this ClaimsPrincipal user) =>
        AppRoleExtensions.TryParseWire(user.FindFirst(ClaimTypes.Role)?.Value, out var role) ? role : null;
}
