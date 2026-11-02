using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using static Utsav.Api.Endpoints.AdvanceEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// A committee's own admins inviting people. An invite is a one-time code bound to
/// one email address and one role; the page turns it into a link
/// (<c>/#invite=CODE</c>) whose code never reaches a server log. Only the hash is
/// stored, so the code is shown once, in the response that creates it.
///
/// Anyone who may manage users can invite — but only an Admin can mint an Admin
/// invite, so a Secretariat member cannot promote themselves or a friend.
/// </summary>
public static class InviteEndpoints
{
    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invites").RequirePermission(Permission.ManageUsers);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapDelete("/{id:guid}", RevokeAsync);
        return app;
    }

    /// <summary>Invites still waiting to be used. Never the codes themselves.</summary>
    private static async Task<IResult> ListAsync(UtsavDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await db.OrgInvites.AsNoTracking()
            .Where(i => i.OrgId == db.TenantId && i.RedeemedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new { i.Id, i.Email, i.Role, expires_at = i.ExpiresAt, created_at = i.CreatedAt })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateAsync(
        JsonElement body, ClaimsPrincipal user, HttpContext http, UtsavDbContext db, InviteService invites,
        IAuditSink audit, CancellationToken ct)
    {
        var email = TryString(body, "email", out var e) ? e.Trim() : "";
        if (!TenantProvisioner.IsPlausibleEmail(email))
        {
            return Results.Json(new { error = "A valid email address is required." }, statusCode: 400);
        }

        if (!TryString(body, "role", out var requested) || !AppRoleExtensions.TryParseWire(requested, out var role))
        {
            return Results.Json(
                new { error = $"role must be one of: {string.Join(", ", AppRoleExtensions.AllWireValues)}" }, statusCode: 400);
        }

        if (role == AppRole.Admin && user.CallerRole() != AppRole.Admin)
        {
            return Results.Json(new { error = "Only an Admin can invite another Admin." }, statusCode: 403);
        }

        var now = DateTime.UtcNow;
        var wire = role.ToWire();
        var normalised = email.ToLowerInvariant();

        // A newer invite for the same address replaces the old one: a lost code may be in someone's chat history.
        await db.OrgInvites
            .Where(i => i.OrgId == db.TenantId && i.Email == normalised && i.RedeemedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, now), ct);

        var code = invites.Add(db.TenantId, email, wire, now);
        await db.SaveChangesAsync(ct);

        try
        {
            await audit.WriteAsync(DynamicSql.BuildEntry(
                "org_invites", "INSERT INTO", AuditEntry.Success, null, normalised,
                before: null, after: new Dictionary<string, object?> { ["email"] = normalised, ["role"] = wire },
                http, db.TenantId), ct);
        }
        catch
        {
            // Audit capture is never why a real mutation fails.
        }

        return Results.Ok(new { invite_code = code, invite_expires_at = now + InviteService.Lifetime, email = normalised, role = wire });
    }

    private static async Task<IResult> RevokeAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var changed = await db.OrgInvites
            .Where(i => i.Id == id && i.OrgId == db.TenantId && i.RedeemedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, now), ct);
        return changed == 0
            ? Results.Json(new { error = "No such outstanding invitation." }, statusCode: 404)
            : Results.Ok(new { ok = true });
    }
}
