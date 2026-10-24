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

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest? request, UtsavDbContext db, SessionService sessions, CancellationToken ct)
    {
        var email = (request?.Email ?? "").Trim();
        var password = request?.Password ?? "";

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !ScryptPasswordHasher.Verify(password, user.PasswordHash))
        {
            return Results.Json(new { error = "Invalid email or password" }, statusCode: 401);
        }

        return await IssueSessionAsync(user.Id, sessions, ct);
    }

    private static async Task<IResult> SignupAsync(
        [FromBody] SignupRequest? request, ExternalIdentityService identities, SessionService sessions, CancellationToken ct)
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
            return await IssueSessionAsync(user.Id, sessions, ct);
        }
        catch (ExternalIdentityException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 400);
        }
    }

    private static async Task<IResult> GoogleAsync(
        [FromBody] GoogleRequest? request, ExternalIdentityService identities, SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Credential))
        {
            return Results.Json(new { error = "Missing Google credential" }, statusCode: 400);
        }

        try
        {
            var user = await identities.FromGoogleAsync(request.Credential, ct);
            return await IssueSessionAsync(user.Id, sessions, ct);
        }
        catch (ExternalIdentityException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 401);
        }
    }

    private static async Task<IResult> EntraAsync(
        [FromBody] EntraRequest? request, ExternalIdentityService identities, SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.IdToken))
        {
            return Results.Json(new { error = "Missing Microsoft credential" }, statusCode: 400);
        }

        try
        {
            var user = await identities.FromEntraAsync(request.IdToken, ct);
            return await IssueSessionAsync(user.Id, sessions, ct);
        }
        catch (ExternalIdentityException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 401);
        }
    }

    /// <summary>
    /// Every sign-in path ends the same way — mint an opaque session token and
    /// return the same <c>{token, user, org, role, status}</c> envelope, so the
    /// front end doesn't care which button was pressed.
    /// </summary>
    private static async Task<IResult> IssueSessionAsync(Guid userId, SessionService sessions, CancellationToken ct)
    {
        var token = await sessions.CreateSessionAsync(userId, ct);
        var session = await sessions.LoadSessionAsync(token, ct);
        return Results.Ok(new AuthResponse(token, session!.User, session.Org, session.Role, session.Status));
    }

    private static IResult MeAsync(HttpContext http)
    {
        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        return Results.Ok(session);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, SessionService sessions, CancellationToken ct)
    {
        var token = http.User.FindFirst(LegacySessionAuthenticationDefaults.TokenClaimType)!.Value;
        await sessions.DestroySessionAsync(token, ct);
        return Results.Ok(new { ok = true });
    }

    private sealed record LoginRequest(string? Email, string? Password);

    private sealed record SignupRequest(string? Email, string? Password, string? FullName);

    private sealed record GoogleRequest(string? Credential);

    private sealed record EntraRequest(string? IdToken);

    private sealed record AuthResponse(string Token, SessionUser User, SessionOrganization? Org, string? Role, string Status);
}
