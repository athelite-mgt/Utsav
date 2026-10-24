using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authentication;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Public — these are how a token is obtained in the first place.
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/signup", SignupAsync).AllowAnonymous();
        group.MapPost("/google", GoogleAsync).AllowAnonymous();
        group.MapPost("/entra", EntraAsync).AllowAnonymous();

        // Authenticated, but membership status doesn't matter here — a
        // pending/denied user still needs /me to find that out.
        group.MapGet("/me", MeAsync).RequireAuthorization();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();

        // Both are authenticated but deliberately NOT behind ActiveMember: the
        // people who need them have no active committee yet. Switching is how a
        // user with several committees and none chosen gets one; requesting
        // access is how a user with none gets started.
        group.MapPost("/switch-org", SwitchOrgAsync).RequireAuthorization();
        group.MapPost("/request-access", RequestAccessAsync).RequireAuthorization();
        group.MapPost("/redeem-invite", RedeemInviteAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest? request, HttpContext http, UtsavDbContext db, SessionService sessions, CancellationToken ct)
    {
        var email = (request?.Email ?? "").Trim();
        var password = request?.Password ?? "";

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !ScryptPasswordHasher.Verify(password, user.PasswordHash))
        {
            return Results.Json(new { error = "Invalid email or password" }, statusCode: 401);
        }

        return await IssueSessionAsync(user.Id, http, sessions, ct);
    }

    private static async Task<IResult> SignupAsync(
        [FromBody] SignupRequest? request, HttpContext http, ExternalIdentityService identities, SessionService sessions, CancellationToken ct)
    {
        var email = (request?.Email ?? "").Trim();
        var password = request?.Password ?? "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return Results.Json(new { error = "Email and password are required" }, statusCode: 400);
        }
        if (password.Length < 6)
        {
            return Results.Json(new { error = "Password must be at least 6 characters" }, statusCode: 400);
        }

        try
        {
            var user = await identities.CreateLocalUserAsync(email, password, request?.FullName, ct);
            return await IssueSessionAsync(user.Id, http, sessions, ct);
        }
        catch (ExternalIdentityException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 400);
        }
    }

    private static async Task<IResult> GoogleAsync(
        [FromBody] GoogleRequest? request, HttpContext http, ExternalIdentityService identities, SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Credential))
        {
            return Results.Json(new { error = "Missing Google credential" }, statusCode: 400);
        }

        try
        {
            var user = await identities.FromGoogleAsync(request.Credential, ct);
            return await IssueSessionAsync(user.Id, http, sessions, ct);
        }
        catch (ExternalIdentityException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 401);
        }
    }

    private static async Task<IResult> EntraAsync(
        [FromBody] EntraRequest? request, HttpContext http, ExternalIdentityService identities, SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.IdToken))
        {
            return Results.Json(new { error = "Missing Microsoft credential" }, statusCode: 400);
        }

        try
        {
            var user = await identities.FromEntraAsync(request.IdToken, ct);
            return await IssueSessionAsync(user.Id, http, sessions, ct);
        }
        catch (ExternalIdentityException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 401);
        }
    }

    /// <summary>
    /// Every sign-in path ends the same way: mint a session, hand the browser its
    /// credential as an httpOnly cookie, and return the same envelope, so the
    /// front end doesn't care which button was pressed.
    ///
    /// The token itself is NOT in the response body. Putting it there would put
    /// it straight back within reach of any script on the page, which is exactly
    /// what moving it into a cookie is meant to prevent. What the page gets
    /// instead is the CSRF token, which it needs and which cannot be turned back
    /// into the session.
    /// </summary>
    private static async Task<IResult> IssueSessionAsync(
        Guid userId, HttpContext http, SessionService sessions, CancellationToken ct)
    {
        var token = await sessions.CreateSessionAsync(userId, ct);
        var session = await sessions.LoadSessionAsync(token, ct);

        SessionCookie.Issue(http, token, DateTimeOffset.UtcNow + SessionService.AbsoluteLifetime);
        return Results.Ok(Envelope(session!, token));
    }

    private static AuthResponse Envelope(SessionPayload session, string rawToken) => new(
        session.User, session.Org, session.Role, session.Status, session.Memberships,
        session.IsPlatformAdmin, SessionTokens.CsrfFor(rawToken));

    private static string RawToken(HttpContext http) =>
        http.User.FindFirst(LegacySessionAuthenticationDefaults.TokenClaimType)!.Value;

    private static IResult MeAsync(HttpContext http)
    {
        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        return Results.Ok(Envelope(session, RawToken(http)));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, SessionService sessions, CancellationToken ct)
    {
        await sessions.DestroySessionAsync(RawToken(http), ct);
        SessionCookie.Clear(http);
        return Results.Ok(new { ok = true });
    }

    /// <summary>
    /// Pins the caller's session to one of their committees.
    ///
    /// Deliberately a server round trip rather than a client-side selection:
    /// the tenant every later request runs under comes from the session row, so
    /// this is the only way to change it, and membership is re-verified here
    /// before anything moves.
    /// </summary>
    private static async Task<IResult> SwitchOrgAsync(
        [FromBody] SwitchOrgRequest? request, HttpContext http, SessionService sessions, CancellationToken ct)
    {
        if (request?.OrgId is not { } orgId || orgId == Guid.Empty)
        {
            return Results.Json(new { error = "org_id is required" }, statusCode: 400);
        }

        var token = http.User.FindFirst(LegacySessionAuthenticationDefaults.TokenClaimType)!.Value;
        var userId = Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        if (!await sessions.SwitchOrgAsync(userId, orgId, token, ct))
        {
            // 403, not 404: telling an outsider whether a committee id exists is
            // itself a small leak.
            return Results.Json(new { error = "You are not a member of that committee" }, statusCode: 403);
        }

        return Results.Ok(Envelope((await sessions.LoadSessionAsync(token, ct))!, token));
    }

    /// <summary>
    /// Asks to join a committee, by its slug. Creates a PENDING membership;
    /// the committee's own admin still has to approve it. There is no default
    /// committee, so signing up no longer implies a request anywhere.
    /// </summary>
    private static async Task<IResult> RequestAccessAsync(
        [FromBody] RequestAccessRequest? request, HttpContext http, UtsavDbContext db,
        ExternalIdentityService identities, CancellationToken ct)
    {
        var slug = request?.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
        {
            return Results.Json(new { error = "slug is required" }, statusCode: 400);
        }

        var org = await db.Organizations.AsNoTracking()
            .Where(o => o.Slug == slug && o.IsActive)
            .Select(o => new { o.Id, o.Name, o.Slug })
            .SingleOrDefaultAsync(ct);
        if (org is null)
        {
            return Results.Json(new { error = "No such committee" }, statusCode: 404);
        }

        var userId = Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await identities.RequestAccessAsync(userId, org.Id, ct);
        return Results.Ok(new { ok = true, org });
    }

    /// <summary>
    /// Redeems the one-time code a platform operator gave a committee's first
    /// administrator. Needs the code AND an account registered under the
    /// address it was issued for, so a stranger who merely registers that
    /// address first gets nothing, and a wrong guess never uses the code up.
    /// </summary>
    private static async Task<IResult> RedeemInviteAsync(
        [FromBody] RedeemInviteRequest? request, HttpContext http, InviteService invites,
        SessionService sessions, CancellationToken ct)
    {
        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        var result = await invites.RedeemAsync(session.User.Id, session.User.Email, request?.Code, ct);

        switch (result.Outcome)
        {
            case RedeemOutcome.UnknownCode:
                return Results.Json(new { error = "That code isn't valid." }, statusCode: 404);
            case RedeemOutcome.AlreadyUsed:
                return Results.Json(new { error = "That code has already been used." }, statusCode: 400);
            case RedeemOutcome.Expired:
                return Results.Json(new { error = "That code has expired. Ask for a new one." }, statusCode: 400);
            case RedeemOutcome.WrongAccount:
                return Results.Json(new { error = "That code was issued for a different email address." }, statusCode: 403);
        }

        // They came here to work in this committee: take them straight in.
        var token = http.User.FindFirst(LegacySessionAuthenticationDefaults.TokenClaimType)!.Value;
        await sessions.SwitchOrgAsync(session.User.Id, result.OrgId!.Value, token, ct);
        return Results.Ok(Envelope((await sessions.LoadSessionAsync(token, ct))!, token));
    }

    private sealed record RedeemInviteRequest(string? Code);

    private sealed record SwitchOrgRequest(Guid? OrgId);

    private sealed record RequestAccessRequest(string? Slug);

    private sealed record LoginRequest(string? Email, string? Password);

    private sealed record SignupRequest(string? Email, string? Password, string? FullName);

    private sealed record GoogleRequest(string? Credential);

    private sealed record EntraRequest(string? IdToken);

    private sealed record AuthResponse(
        SessionUser User, SessionOrganization? Org, string? Role, string Status,
        IReadOnlyList<SessionMembership> Memberships, bool IsPlatformAdmin, string CsrfToken);
}
