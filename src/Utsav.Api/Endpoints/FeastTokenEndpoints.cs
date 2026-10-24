using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Api.Reports;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using static Utsav.Api.Endpoints.AdvanceEndpoints;
using static Utsav.Api.Endpoints.ModuleEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// The QR-based feast entry-token lifecycle: print a batch of still-blank
/// coupons, assign each one to a donor's <see cref="Feast"/> record once it's
/// physically handed out, then redeem it once at the fooding-area gate.
/// <c>unassigned -&gt; assigned -&gt; redeemed</c>, with <c>void</c> as the one side
/// exit for a lost or cancelled token — see <see cref="FeastTokenStatus"/>.
///
/// Printing is the one bulk write here (tens to hundreds of rows at once), so
/// it goes through EF directly inside a transaction that also allocates the
/// run's sequence numbers, with one summary audit entry for the whole batch
/// (<see cref="DynamicSql.AuditFeastTokenPrintAsync"/>) rather than one per
/// token. Every other write is a single-row state transition, done the same
/// way the rest of the codebase does single-row writes: a conditional
/// <c>UPDATE ... WHERE status = @expected</c> through
/// <see cref="DynamicSql.ExecuteAsync"/>, so a zero-row result always means
/// "someone already changed this" rather than silently succeeding twice.
/// </summary>
public static class FeastTokenEndpoints
{
    /// <summary>Generous enough for a large committee's full door-to-door run without inviting an accidental fat-fingered batch.</summary>
    private const int MaxPrintCount = 2000;

    public static IEndpointRouteBuilder MapFeastTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var member = app.MapGroup("/api/feast_tokens").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        var write = app.MapGroup("/api/feast_tokens").RequirePermission(Permission.WriteIncome);

        member.MapGet("", ListAsync);
        member.MapGet("/summary", SummaryAsync);
        member.MapGet("/lookup/{code}", LookupAsync);

        write.MapPost("/print", PrintAsync);
        write.MapPost("/assign", AssignAsync);
        write.MapPost("/redeem", RedeemAsync);
        write.MapPost("/void", VoidAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid year_id, string? status, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await QueryAsync(db, $"""
            SELECT ft.*, d.name AS donor_name, d.surname AS donor_surname
              FROM feast_tokens ft
              LEFT JOIN feasts f ON f.id = ft.feast_id
              LEFT JOIN donors d ON d.id = f.donor_id
             WHERE ft.org_id = @org AND ft.year_id = @year
               {(string.IsNullOrWhiteSpace(status) ? "" : "AND ft.status = @status")}
             ORDER BY ft.seq_no ASC
            """,
            string.IsNullOrWhiteSpace(status)
                ? [P("@year", year_id), Org()]
                : [P("@year", year_id), Org(), P("@status", status)],
            ct);

        return Results.Ok(rows);
    }

    /// <summary>
    /// Read-only lookup for the gate scanner — a camera decode "gets" the
    /// token's current state so the volunteer can see who it belongs to
    /// before anything happens; <see cref="RedeemAsync"/> is the only call
    /// that actually consumes it, and only fires on an explicit confirm tap.
    /// </summary>
    private static async Task<IResult> LookupAsync(string code, UtsavDbContext db, CancellationToken ct)
    {
        var token = (await TokenDetailByCodeAsync(db, code, ct)).FirstOrDefault();
        return token is null
            ? Results.NotFound(new { error = $"No token with code {code}" })
            : Results.Ok(token);
    }

    private static async Task<IResult> SummaryAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await QueryAsync(db,
            "SELECT status, COUNT(*) AS count FROM feast_tokens WHERE org_id = @org AND year_id = @year GROUP BY status",
            [P("@year", year_id), Org()], ct);

        var counts = new Dictionary<string, int>
        {
            [FeastTokenStatus.Unassigned] = 0,
            [FeastTokenStatus.Assigned] = 0,
            [FeastTokenStatus.Redeemed] = 0,
            [FeastTokenStatus.Void] = 0,
        };
        foreach (var row in rows)
        {
            if (row["status"] is string s && counts.ContainsKey(s))
            {
                counts[s] = Convert.ToInt32(row["count"]);
            }
        }

        return Results.Ok(new
        {
            unassigned = counts[FeastTokenStatus.Unassigned],
            assigned = counts[FeastTokenStatus.Assigned],
            redeemed = counts[FeastTokenStatus.Redeemed],
            voided = counts[FeastTokenStatus.Void],
            total = counts.Values.Sum(),
        });
    }

    /// <summary>
    /// Asks for <c>year_id</c> and <c>count</c>, returns the sheet PDF
    /// directly (see <see cref="FeastTokenSheetPdf"/>) — ten coupons per
    /// page, committee name, puja year and today's date on each. Numbering
    /// always continues from this year's highest existing <c>seq_no</c>, so a
    /// second print run for more tokens picks up where the first left off
    /// rather than colliding with or restarting it.
    /// </summary>
    private static async Task<IResult> PrintAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!body.TryGetProperty("year_id", out var yearEl) || !Guid.TryParse(yearEl.GetString(), out var yearId))
        {
            return Results.Json(new { error = "year_id is required" }, statusCode: 400);
        }
        if (!body.TryGetProperty("count", out var countEl) || countEl.ValueKind != JsonValueKind.Number)
        {
            return Results.Json(new { error = "count is required" }, statusCode: 400);
        }

        var count = countEl.GetInt32();
        if (count < 1 || count > MaxPrintCount)
        {
            return Results.Json(new { error = $"count must be between 1 and {MaxPrintCount}" }, statusCode: 400);
        }

        var org = SessionService.DefaultOrgId;
        var yearRow = (await QueryAsync(db, """
            SELECT py.year, o.name AS org_name
              FROM puja_years py JOIN organizations o ON o.id = py.org_id
             WHERE py.id = @year AND py.org_id = @org
            """, [P("@year", yearId), Org()], ct)).FirstOrDefault();

        if (yearRow is null)
        {
            return Results.NotFound(new { error = "Puja year not found" });
        }

        var pujaYear = Convert.ToInt32(yearRow["year"]);
        var orgName = yearRow["org_name"] as string ?? "Puja Committee";

        List<FeastToken> created = [];
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // UPDLOCK+HOLDLOCK: two committee members printing at the same
            // moment must still get disjoint sequence ranges, not a shared
            // starting point.
            var maxRow = await QueryAsync(db,
                "SELECT ISNULL(MAX(seq_no), 0) AS max_seq FROM feast_tokens WITH (UPDLOCK, HOLDLOCK) WHERE org_id = @org AND year_id = @year",
                [P("@year", yearId), Org()], ct);
            var startSeq = Convert.ToInt32(maxRow.FirstOrDefault()?["max_seq"] ?? 0);

            created = Enumerable.Range(1, count).Select(i => new FeastToken
            {
                Id = Guid.NewGuid(),
                OrgId = org,
                YearId = yearId,
                SeqNo = startSeq + i,
                Code = $"F{pujaYear}-{startSeq + i:0000}",
                Status = FeastTokenStatus.Unassigned,
                CreatedAt = DateTime.UtcNow,
            }).ToList();

            db.FeastTokens.AddRange(created);
            await db.SaveChangesAsync(ct);

            var label = created.Count == 1
                ? created[0].Code
                : $"{created[0].Code} – {created[^1].Code} ({created.Count} tokens)";
            await DynamicSql.AuditFeastTokenPrintAsync(label, new Dictionary<string, object?>
            {
                ["year_id"] = yearId,
                ["count"] = created.Count,
                ["from_code"] = created[0].Code,
                ["to_code"] = created[^1].Code,
            }, ct);

            await transaction.CommitAsync(ct);
        });

        var bytes = FeastTokenSheetPdf.Render(created, orgName, pujaYear, DateTime.UtcNow);
        return Results.File(bytes, "application/pdf", $"feast-tokens-{pujaYear}-{created[0].Code}-to-{created[^1].Code}.pdf");
    }

    /// <summary>Links a printed-but-blank token to the donor whose door it ended up at.</summary>
    private static async Task<IResult> AssignAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "code", out var code))
        {
            return Results.Json(new { error = "code is required" }, statusCode: 400);
        }
        if (!body.TryGetProperty("feast_id", out var feastIdEl) || !Guid.TryParse(feastIdEl.GetString(), out var feastId))
        {
            return Results.Json(new { error = "feast_id is required" }, statusCode: 400);
        }

        var token = (await TokenDetailByCodeAsync(db, code, ct)).FirstOrDefault();
        if (token is null)
        {
            return Results.NotFound(new { error = $"No token with code {code}" });
        }
        if ((string)token["status"]! != FeastTokenStatus.Unassigned)
        {
            return Results.Json(new { error = $"Token {code} is already {token["status"]}, not available for assignment" }, statusCode: 409);
        }

        var feast = (await QueryAsync(db, """
            SELECT f.year_id, d.name AS donor_name, d.surname AS donor_surname
              FROM feasts f JOIN donors d ON d.id = f.donor_id
             WHERE f.id = @id AND f.org_id = @org
            """, [P("@id", feastId), Org()], ct)).FirstOrDefault();

        if (feast is null)
        {
            return Results.NotFound(new { error = "Feast donor record not found" });
        }
        if ((Guid)feast["year_id"]! != (Guid)token["year_id"]!)
        {
            return Results.Json(new { error = "That donor's feast record is for a different puja year" }, statusCode: 400);
        }

        // The WHERE-side status check is an inlined literal, not a bound
        // parameter — it's a fixed constant this code controls, never
        // request text, so there's no injection surface. Binding it instead
        // would give it some parameter name that doesn't match any real
        // column, and DynamicSql's audit diff (see CrudEndpoints.UpdateAsync's
        // own remarks on this) recovers each changed field's name from its
        // parameter name — a non-column name would show up in History as a
        // nonsense field rather than the real "status" change.
        var rows = await DynamicSql.ExecuteAsync(db, $"""
            UPDATE feast_tokens SET feast_id = @feast_id, status = @status, assigned_at = @assigned_at
             WHERE id = @id AND org_id = @org AND status = '{FeastTokenStatus.Unassigned}'
            """,
            [
                P("@feast_id", feastId), P("@status", FeastTokenStatus.Assigned), P("@assigned_at", DateTime.UtcNow),
                P("@id", (Guid)token["id"]!), Org(),
            ], ct);

        if (rows == 0)
        {
            return Results.Json(new { error = $"Token {code} is no longer available for assignment" }, statusCode: 409);
        }

        return Results.Ok((await TokenDetailAsync(db, (Guid)token["id"]!, ct)).FirstOrDefault());
    }

    /// <summary>
    /// One-time redemption at the gate. Only an <c>assigned</c> token can be
    /// redeemed — an unassigned one has no donor to verify against, and a
    /// redeemed/void one is terminal — so every rejection reason is reported
    /// back explicitly rather than as a generic failure, since a gate
    /// volunteer needs to know whether to re-scan, ask for a different
    /// token, or turn the person away.
    /// </summary>
    private static async Task<IResult> RedeemAsync(JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "code", out var code))
        {
            return Results.Json(new { error = "code is required" }, statusCode: 400);
        }

        var token = (await TokenDetailByCodeAsync(db, code, ct)).FirstOrDefault();
        if (token is null)
        {
            return Results.NotFound(new { error = $"No token with code {code}" });
        }

        var status = (string)token["status"]!;
        switch (status)
        {
            case FeastTokenStatus.Redeemed:
                return Results.Json(new
                {
                    error = "Already redeemed",
                    donor_name = DonorLabel(token),
                    redeemed_at = token["redeemed_at"],
                    redeemed_by = token["redeemed_by_name"],
                }, statusCode: 409);
            case FeastTokenStatus.Void:
                return Results.Json(new { error = $"Token {code} has been voided" }, statusCode: 409);
            case FeastTokenStatus.Unassigned:
                return Results.Json(new { error = $"Token {code} has not been assigned to a donor yet" }, statusCode: 409);
        }

        var userId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (Guid?)null;
        var userName = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;

        // See the matching comment in AssignAsync — the WHERE-side status
        // check is a controlled literal, not a parameter, so the audit
        // diff's field-name recovery sees only the real columns being written.
        var rows = await DynamicSql.ExecuteAsync(db, $"""
            UPDATE feast_tokens
               SET status = @status, redeemed_at = @redeemed_at, redeemed_by_user_id = @redeemed_by_user_id, redeemed_by_name = @redeemed_by_name
             WHERE id = @id AND org_id = @org AND status = '{FeastTokenStatus.Assigned}'
            """,
            [
                P("@status", FeastTokenStatus.Redeemed), P("@redeemed_at", DateTime.UtcNow),
                P("@redeemed_by_user_id", (object?)userId ?? DBNull.Value), P("@redeemed_by_name", (object?)userName ?? DBNull.Value),
                P("@id", (Guid)token["id"]!), Org(),
            ], ct);

        if (rows == 0)
        {
            return Results.Json(new { error = $"Token {code} changed state — please rescan" }, statusCode: 409);
        }

        return Results.Ok((await TokenDetailAsync(db, (Guid)token["id"]!, ct)).FirstOrDefault());
    }

    /// <summary>The one side exit — a lost or cancelled token, from any state except already-void.</summary>
    private static async Task<IResult> VoidAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "code", out var code))
        {
            return Results.Json(new { error = "code is required" }, statusCode: 400);
        }

        var token = (await TokenDetailByCodeAsync(db, code, ct)).FirstOrDefault();
        if (token is null)
        {
            return Results.NotFound(new { error = $"No token with code {code}" });
        }
        if ((string)token["status"]! == FeastTokenStatus.Void)
        {
            return Results.Json(new { error = $"Token {code} is already void" }, statusCode: 409);
        }

        var rows = await DynamicSql.ExecuteAsync(db,
            $"UPDATE feast_tokens SET status = @status WHERE id = @id AND org_id = @org AND status <> '{FeastTokenStatus.Void}'",
            [P("@status", FeastTokenStatus.Void), P("@id", (Guid)token["id"]!), Org()], ct);

        if (rows == 0)
        {
            return Results.Json(new { error = $"Token {code} changed state — please retry" }, statusCode: 409);
        }

        return Results.Ok((await TokenDetailAsync(db, (Guid)token["id"]!, ct)).FirstOrDefault());
    }

    private static Task<List<Dictionary<string, object?>>> TokenDetailByCodeAsync(UtsavDbContext db, string code, CancellationToken ct) =>
        QueryAsync(db, """
            SELECT ft.*, d.name AS donor_name, d.surname AS donor_surname, d.contact AS donor_contact
              FROM feast_tokens ft
              LEFT JOIN feasts f ON f.id = ft.feast_id
              LEFT JOIN donors d ON d.id = f.donor_id
             WHERE ft.code = @code AND ft.org_id = @org
            """, [P("@code", code), Org()], ct);

    private static Task<List<Dictionary<string, object?>>> TokenDetailAsync(UtsavDbContext db, Guid id, CancellationToken ct) =>
        QueryAsync(db, """
            SELECT ft.*, d.name AS donor_name, d.surname AS donor_surname, d.contact AS donor_contact
              FROM feast_tokens ft
              LEFT JOIN feasts f ON f.id = ft.feast_id
              LEFT JOIN donors d ON d.id = f.donor_id
             WHERE ft.id = @id AND ft.org_id = @org
            """, [P("@id", id), Org()], ct);

    private static string? DonorLabel(Dictionary<string, object?> row) =>
        row.GetValueOrDefault("donor_name") is string name
            ? row.GetValueOrDefault("donor_surname") is string surname && !string.IsNullOrWhiteSpace(surname)
                ? $"{name} {surname}"
                : name
            : null;
}
