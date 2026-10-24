using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Data;

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

        platform.MapGet("/festivals", () => Results.Ok(FestivalTemplates.Known
            .Select(id => new { id, name = FestivalTemplates.For(id).DisplayName })));

        platform.MapGet("/committees", ListAsync);
        platform.MapPost("/committees", CreateAsync);
        platform.MapPost("/committees/{id:guid}/reissue-invite", ReissueInviteAsync);

        return app;
    }

    /// <summary>A directory of committees. Never their books.</summary>
    private static async Task<IResult> ListAsync(UtsavDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var rows = await db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.Slug,
                o.City,
                o.IsActive,
                o.CreatedAt,
                Members = db.OrgMembers.Count(m => m.OrgId == o.Id && m.Status == "active"),
                PendingInvite = db.OrgInvites.Any(i => i.OrgId == o.Id && i.RedeemedAt == null && i.ExpiresAt > now),
            })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCommitteeRequest? request, HttpContext http, TenantProvisioner provisioner,
        IAuditSink audit, CancellationToken ct)
    {
        if (request is null)
        {
            return Results.Json(new { error = "A request body is required." }, statusCode: 400);
        }

        var year = request.FirstYear is > 2000 and < 2100 ? request.FirstYear.Value : DateTime.UtcNow.Year;
        var festival = string.IsNullOrWhiteSpace(request.Festival) ? "durga" : request.Festival;

        ProvisionResult result;
        try
        {
            result = await provisioner.ProvisionAsync(new ProvisionRequest(
                request.Name ?? "", request.Slug, request.City, festival, year, request.FirstAdminEmail ?? ""), ct);
        }
        catch (InvalidProvisionRequestException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 400);
        }
        catch (SlugTakenException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 409);
        }

        // Filed under the NEW committee, so its own History opens with who
        // created it. The invite code is deliberately absent from the record.
        try
        {
            await audit.WriteAsync(DynamicSql.BuildEntry(
                "organizations", "INSERT INTO", AuditEntry.Success, null, request.Name,
                before: null,
                after: new Dictionary<string, object?>
                {
                    ["name"] = request.Name,
                    ["slug"] = result.Slug,
                    ["festival"] = festival,
                    ["first_year"] = year,
                    ["first_admin_email"] = request.FirstAdminEmail,
                },
                http, result.OrgId), ct);
        }
        catch
        {
            // Audit capture is never why a real mutation fails.
        }

        return Results.Ok(new
        {
            id = result.OrgId,
            slug = result.Slug,
            invite_code = result.InviteCode,
            invite_expires_at = result.InviteExpiresAt,
        });
    }

    /// <summary>
    /// Replaces a lost invite code, but ONLY while the committee has no active
    /// members. If this worked on a committee with people in it, an operator
    /// could mint an admin invite for their own address in any committee and
    /// read its books, and the platform capability is meant to grant no access
    /// to a committee's data. Once someone is in, the committee's own admins
    /// manage who else joins.
    /// </summary>
    private static async Task<IResult> ReissueInviteAsync(
        Guid id, [FromBody] ReissueInviteRequest? request, HttpContext http, UtsavDbContext db,
        InviteService invites, IAuditSink audit, CancellationToken ct)
    {
        var email = request?.Email?.Trim() ?? "";
        if (!TenantProvisioner.IsPlausibleEmail(email))
        {
            return Results.Json(new { error = "A valid email address is required." }, statusCode: 400);
        }

        var org = await db.Organizations.AsNoTracking().Where(o => o.Id == id).Select(o => new { o.Id, o.Name }).SingleOrDefaultAsync(ct);
        if (org is null)
        {
            return Results.Json(new { error = "No such committee." }, statusCode: 404);
        }

        if (await db.OrgMembers.AnyAsync(m => m.OrgId == id && m.Status == "active", ct))
        {
            return Results.Json(
                new { error = "This committee already has members. Its own admins manage who else joins." },
                statusCode: 409);
        }

        var now = DateTime.UtcNow;
        string code = "";
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Whatever was outstanding stops working: a lost code may be sitting
            // in someone's chat history.
            await db.OrgInvites
                .Where(i => i.OrgId == id && i.RedeemedAt == null && i.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, now), ct);

            code = invites.Add(id, email, "admin", now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        try
        {
            await audit.WriteAsync(DynamicSql.BuildEntry(
                "org_invites", "INSERT INTO", AuditEntry.Success, null, org.Name,
                before: null,
                after: new Dictionary<string, object?> { ["email"] = email.ToLowerInvariant(), ["role"] = "admin" },
                http, id), ct);
        }
        catch
        {
            // Audit capture is never why a real mutation fails.
        }

        return Results.Ok(new { invite_code = code, invite_expires_at = now + InviteService.Lifetime });
    }

    private sealed record ReissueInviteRequest(string? Email);

    private sealed record CreateCommitteeRequest(
        string? Name, string? Slug, string? City, string? Festival, int? FirstYear, string? FirstAdminEmail);
}
