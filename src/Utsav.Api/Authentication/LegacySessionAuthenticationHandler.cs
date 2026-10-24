using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Tenancy;

namespace Utsav.Api.Authentication;

public static class LegacySessionAuthenticationDefaults
{
    public const string SchemeName = "LegacySession";

    /// <summary>Set once <see cref="SessionPayload.Status"/> is known, so policies can require it without a second DB round trip.</summary>
    public const string StatusClaimType = "puja:status";

    /// <summary>The raw bearer token, so /auth/logout can destroy the exact session without re-parsing the header.</summary>
    public const string TokenClaimType = "puja:token";
}

/// <summary>
/// Ported from <c>requireAuth</c> in <c>server/auth.mjs</c>: an opaque bearer
/// token looked up in the <c>sessions</c> table — not a JWT, so this is a
/// custom scheme rather than <c>AddJwtBearer</c>. Kept as its own scheme
/// (rather than folded into endpoint logic) so it composes with
/// policy-based authorization the same way the future Entra JWT scheme will
/// (TASKLIST §6.1, §4.3) — Auth:Mode can pick between them per request
/// later without every endpoint changing.
///
/// Membership status (active/pending/none) is resolved here too and exposed
/// as a claim, since Node's requireAuth+requireOrg chain needs the same
/// session lookup either way — this just does it once instead of twice.
/// </summary>
public sealed class LegacySessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions,
    TenantContext tenant)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..];
        var session = await sessions.LoadSessionAsync(token, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("Not signed in");
        }

        // Endpoints that already need the full payload (e.g. /auth/me) can
        // read this instead of re-querying for what was just resolved here.
        Context.Items[nameof(SessionPayload)] = session;

        // The one place a tenant enters a member's request. Only an active
        // membership in the pinned committee establishes it, so a pending or
        // revoked member reaches the app with no tenant at all and every data
        // endpoint refuses them.
        if (session.Org is not null && session.Status == "active")
        {
            tenant.Resolve(session.Org.Id);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.User.Id.ToString()),
            new(ClaimTypes.Email, session.User.Email),
            // Falls back to the email itself for the handful of accounts
            // created before a name was required — the audit trail should
            // never show a blank "who".
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(session.User.FullName) ? session.User.Email : session.User.FullName),
            new(LegacySessionAuthenticationDefaults.StatusClaimType, session.Status),
            new(LegacySessionAuthenticationDefaults.TokenClaimType, token),
        };
        if (session.Role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, session.Role));
        }

        var identity = new ClaimsIdentity(claims, LegacySessionAuthenticationDefaults.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), LegacySessionAuthenticationDefaults.SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
