using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
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
        platform.MapGet("/committees/{id:guid}/members", MembersAsync);
        platform.MapPost("/committees/{id:guid}/invites", InviteAsync);
        platform.MapGet("/committees/{id:guid}/requests", RequestsAsync);
        platform.MapPost("/committees/{id:guid}/requests/{memberId:guid}/approve", ApproveFirstAdminAsync);

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
                PendingRequests = db.OrgMembers.Count(m => m.OrgId == o.Id && m.Status == "pending"),
                PendingInvite = db.OrgInvites.Any(i => i.OrgId == o.Id && i.RedeemedAt == null && i.ExpiresAt > now),
            })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    /// <summary>
    /// Who is in a committee — name, email and role. Deliberately nothing else: the
    /// operator can see who runs a committee (and find the admin to ask), never its money.
    /// </summary>
    private static async Task<IResult> MembersAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await (
            from m in db.OrgMembers.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.OrgId == id && m.Status == "active"
            orderby m.CreatedAt
            select new { member_id = m.Id, u.Email, full_name = u.FullName, m.Role }
        ).ToListAsync(ct);
        return Results.Ok(rows);
    }

    /// <summary>Join requests waiting in a committee, for the operator to approve when its own admins can't or don't.</summary>
    private static async Task<IResult> RequestsAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await (
            from m in db.OrgMembers.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.OrgId == id && m.Status == "pending"
            orderby m.CreatedAt
            select new { member_id = m.Id, u.Email, full_name = u.FullName }
        ).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static bool TryRole(JsonElement body, string fallback, out AppRole role)
    {
        var wire = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() ?? fallback
            : fallback;
        return AppRoleExtensions.TryParseWire(wire, out role);
    }

    private static async Task AuditAsync(
        IAuditSink audit, HttpContext http, string table, string verb, string orgName, Guid orgId,
        Dictionary<string, object?> after, CancellationToken ct)
    {
        try
        {
            after["by_platform_operator"] = true;
            await audit.WriteAsync(DynamicSql.BuildEntry(table, verb, AuditEntry.Success, null, orgName, before: null, after: after, http, orgId), ct);
        }
        catch
        {
            // Audit capture is never why a real mutation fails.
        }
    }

    /// <summary>
    /// Approves a join request with a chosen role (default Admin for an empty committee, Viewer otherwise).
    /// Recorded in the committee's own History as done by the platform operator.
    /// </summary>
    private static async Task<IResult> ApproveFirstAdminAsync(
        Guid id, Guid memberId, JsonElement body, HttpContext http, UtsavDbContext db, IAuditSink audit, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().Where(o => o.Id == id).Select(o => new { o.Id, o.Name }).SingleOrDefaultAsync(ct);
        if (org is null)
        {
            return Results.Json(new { error = "No such committee." }, statusCode: 404);
        }

        var empty = !await db.OrgMembers.AnyAsync(m => m.OrgId == id && m.Status == "active", ct);
        if (!TryRole(body, empty ? "admin" : "viewer", out var role))
        {
            return Results.Json(new { error = $"role must be one of: {string.Join(", ", AppRoleExtensions.AllWireValues)}" }, statusCode: 400);
        }

        var email = await (
            from m in db.OrgMembers.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.Id == memberId && m.OrgId == id && m.Status == "pending"
            select u.Email
        ).SingleOrDefaultAsync(ct);
        if (email is null)
        {
            return Results.Json(new { error = "No such pending request." }, statusCode: 404);
        }

        var wire = role.ToWire();
        await db.OrgMembers
            .Where(m => m.Id == memberId && m.OrgId == id && m.Status == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "active").SetProperty(m => m.Role, wire), ct);

        await AuditAsync(audit, http, "org_members", "UPDATE", org.Name, id,
            new Dictionary<string, object?> { ["email"] = email.ToLowerInvariant(), ["role"] = wire, ["status"] = "active" }, ct);
        return Results.Ok(new { ok = true, email, role = wire });
    }

    /// <summary>An invitation (link + one-time code) into any committee, with a chosen role. Replaces an older one for the same address.</summary>
    private static async Task<IResult> InviteAsync(
        Guid id, JsonElement body, HttpContext http, UtsavDbContext db, InviteService invites, IAuditSink audit, CancellationToken ct)
    {
        var email = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()?.Trim() ?? "" : "";
        if (!TenantProvisioner.IsPlausibleEmail(email))
        {
            return Results.Json(new { error = "A valid email address is required." }, statusCode: 400);
        }

        if (!TryRole(body, "admin", out var role))
        {
            return Results.Json(new { error = $"role must be one of: {string.Join(", ", AppRoleExtensions.AllWireValues)}" }, statusCode: 400);
        }

        var org = await db.Organizations.AsNoTracking().Where(o => o.Id == id).Select(o => new { o.Id, o.Name }).SingleOrDefaultAsync(ct);
        if (org is null)
        {
            return Results.Json(new { error = "No such committee." }, statusCode: 404);
        }

        var now = DateTime.UtcNow;
        var normalised = email.ToLowerInvariant();
        var wire = role.ToWire();
        await db.OrgInvites
            .Where(i => i.OrgId == id && i.Email == normalised && i.RedeemedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, now), ct);

        var code = invites.Add(id, email, wire, now);
        await db.SaveChangesAsync(ct);

        await AuditAsync(audit, http, "org_invites", "INSERT INTO", org.Name, id,
            new Dictionary<string, object?> { ["email"] = normalised, ["role"] = wire }, ct);
        return Results.Ok(new { invite_code = code, invite_expires_at = now + InviteService.Lifetime, email = normalised, role = wire });
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
