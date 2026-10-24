using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using static Utsav.Api.Endpoints.AdvanceEndpoints;
using static Utsav.Api.Endpoints.ModuleEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Membership, committee settings and the year rollover — ported from
/// <c>server/app.mjs</c> lines 162-256 and 738-772. Membership (access
/// requests, roles) needs <see cref="Permission.ManageUsers"/> — Admin and
/// Secretariat; committee configuration (rename, rollover) needs
/// <see cref="Permission.ManageSettings"/> — Admin only.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var manageUsers = app.MapGroup("/api").RequirePermission(Permission.ManageUsers);
        var manageSettings = app.MapGroup("/api").RequirePermission(Permission.ManageSettings);

        manageUsers.MapGet("/access_requests", (UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, """
                SELECT m.id AS member_id, u.id AS user_id, u.email, u.full_name, u.auth_provider, m.created_at
                  FROM org_members m JOIN users u ON u.id = m.user_id
                 WHERE m.org_id=@org AND m.status='pending' ORDER BY m.created_at
                """, [Org()], ct));

        manageUsers.MapPost("/access_requests/{memberId:guid}/approve", ApproveAsync);
        manageUsers.MapPost("/access_requests/{memberId:guid}/deny", async (Guid memberId, UtsavDbContext db, CancellationToken ct) =>
        {
            await DynamicSql.ExecuteAsync(db, "DELETE FROM org_members WHERE id=@id AND org_id=@org", [P("@id", memberId), Org()], ct);
            return Results.Ok(new { ok = true });
        });

        // Active members. Pending requests live under /api/access_requests.
        manageUsers.MapGet("/users", (UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, """
                SELECT u.id, u.email, u.full_name, m.role, m.created_at
                  FROM org_members m JOIN users u ON u.id = m.user_id
                 WHERE m.org_id=@org AND m.status='active' ORDER BY m.created_at
                """, [Org()], ct));

        manageUsers.MapPatch("/users/{id:guid}", ChangeRoleAsync);
        manageSettings.MapPatch("/organizations/current", RenameCommitteeAsync);
        manageSettings.MapPost("/puja_years/{id:guid}/rollover", RolloverAsync);

        return app;
    }

    /// <summary>
    /// The role a request is approved into must be one the permission matrix
    /// knows. That list widened from two values to five — see
    /// <see cref="AppRole"/> — so this validates against the enum rather than a
    /// hardcoded pair of strings.
    /// </summary>
    private static async Task<IResult> ApproveAsync(Guid memberId, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "role", out var requested) || !AppRoleExtensions.TryParseWire(requested, out var role))
        {
            return Results.Json(
                new { error = $"role must be one of: {string.Join(", ", AppRoleExtensions.AllWireValues)}" },
                statusCode: 400);
        }

        var member = await QueryAsync(db, "SELECT id FROM org_members WHERE id=@id AND org_id=@org",
            [P("@id", memberId), Org()], ct);
        if (member.Count == 0)
        {
            return Results.Json(new { error = "Not found" }, statusCode: 404);
        }

        await DynamicSql.ExecuteAsync(db, "UPDATE org_members SET role=@role, status='active' WHERE id=@id AND org_id=@org",
            [P("@role", role.ToWire()), P("@id", memberId), Org()], ct);

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ChangeRoleAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "role", out var requested) || !AppRoleExtensions.TryParseWire(requested, out var role))
        {
            return Results.Json(new { error = "Invalid role" }, statusCode: 400);
        }

        var member = await QueryAsync(db, "SELECT id FROM org_members WHERE org_id=@org AND user_id=@user",
            [Org(), P("@user", id)], ct);
        if (member.Count == 0)
        {
            return Results.Json(new { error = "Not a member of this organization" }, statusCode: 404);
        }

        await DynamicSql.ExecuteAsync(db, "UPDATE org_members SET role=@role WHERE org_id=@org AND user_id=@user",
            [P("@role", role.ToWire()), Org(), P("@user", id)], ct);

        var updated = await QueryAsync(db, """
            SELECT u.id, u.email, u.full_name, m.role
              FROM org_members m JOIN users u ON u.id = m.user_id
             WHERE m.org_id=@org AND m.user_id=@user
            """, [Org(), P("@user", id)], ct);

        return Results.Ok(updated.FirstOrDefault());
    }

    private static async Task<IResult> RenameCommitteeAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "name", out var name))
        {
            return Results.Json(new { error = "Committee name is required" }, statusCode: 400);
        }

        await DynamicSql.ExecuteAsync(db, "UPDATE organizations SET name=@name WHERE id=@org", [P("@name", name.Trim()), Org()], ct);

        var org = await QueryAsync(db, "SELECT id, name, slug FROM organizations WHERE id=@org", [Org()], ct);
        return Results.Ok(org.FirstOrDefault());
    }

    /// <summary>
    /// Starts the next puja year from an existing one: creates the new year row
    /// and carries every donor's subscription forward, using last year's billed
    /// amount as this year's <c>prior_billed</c> and applying the projection
    /// rate to suggest this year's ask.
    ///
    /// Wrapped in a transaction, which the Node original was not — it wrote a
    /// year row, then 183 subscriptions, then 19 budget lines as separate
    /// statements, so a failure midway left a half-rolled-over year that the
    /// duplicate-year check would then refuse to retry.
    /// </summary>
    private static async Task<IResult> RolloverAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        var source = (await QueryAsync(db, "SELECT * FROM puja_years WHERE id=@id AND org_id=@org",
            [P("@id", id), Org()], ct)).FirstOrDefault();
        if (source is null)
        {
            return Results.Json(new { error = "Source year not found" }, statusCode: 404);
        }

        if (!body.TryGetProperty("year", out var yearElement) || !yearElement.TryGetInt32(out var year)
            || year < 2000 || year > 2100)
        {
            return Results.Json(new { error = "A valid target year is required" }, statusCode: 400);
        }

        var clash = await QueryAsync(db, "SELECT id FROM puja_years WHERE org_id=@org AND year=@year",
            [Org(), P("@year", year)], ct);
        if (clash.Count > 0)
        {
            return Results.Json(new { error = $"{year} already exists for this committee" }, statusCode: 400);
        }

        var projectionPct = body.TryGetProperty("projection_pct", out var pct) && pct.ValueKind == JsonValueKind.Number
            ? pct.GetDecimal()
            : Convert.ToDecimal(source["projection_pct"]);
        var name = TryString(body, "name", out var n) ? n : $"Durga Puja {year}";
        var target = body.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetDecimal()
            : source["target"] is decimal existing ? existing : 0m;

        var newYearId = Guid.NewGuid();
        var carried = 0;
        var budgetLinesCarried = 0;

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await DynamicSql.ExecuteAsync(db, """
                INSERT INTO puja_years (id, org_id, year, name, target, projection_pct, is_active)
                VALUES (@id, @org, @year, @name, @target, @pct, 1)
                """,
                [P("@id", newYearId), Org(), P("@year", year), P("@name", name), P("@target", target), P("@pct", projectionPct)], ct);

            // Single active year, mirroring the toggle Settings does client-side.
            await DynamicSql.ExecuteAsync(db, "UPDATE puja_years SET is_active = 0 WHERE id=@id AND org_id=@org", [P("@id", id), Org()], ct);

            var priorSubs = await QueryAsync(db, "SELECT * FROM subscriptions WHERE year_id=@year AND org_id=@org",
                [P("@year", id), Org()], ct);

            foreach (var sub in priorSubs)
            {
                var priorBilled = sub["billed"] is decimal b ? b : 0m;
                var priorCollected = sub["collected"] is decimal c ? c : 0m;
                var category = sub["category"] as string ?? SubscriptionCategory.Membership;

                // The yearly raise is a membership-round convention — every
                // household is asked for a little more than last year. Special
                // donations are negotiated gift by gift, so carrying last
                // year's figure forward untouched is the honest starting point;
                // inflating it would invent an ask nobody agreed to.
                var projected = category == SubscriptionCategory.Special
                    ? priorBilled
                    // Matches the original's round(x * 100) / 100 to the paisa.
                    : Math.Round(priorBilled * (1 + projectionPct / 100m), 2, MidpointRounding.AwayFromZero);

                var history = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["prior_year"] = source["year"],
                    ["prior_billed"] = priorBilled,
                    ["prior_collected"] = priorCollected,
                    ["prior_bill_no"] = sub["bill_no"],
                });

                await DynamicSql.ExecuteAsync(db, """
                    INSERT INTO subscriptions (id, org_id, donor_id, year_id, category, prior_billed, projected, collector_id, history)
                    VALUES (@id, @org, @donor, @year, @category, @priorBilled, @projected, @collector, @history)
                    """,
                    [
                        P("@id", Guid.NewGuid()), Org(), P("@donor", sub["donor_id"]!), P("@year", newYearId),
                        P("@category", category), P("@priorBilled", priorBilled), P("@projected", projected),
                        // Deliberately not carried from the prior year — collector
                        // assignments are made fresh each puja year rather than
                        // defaulting to whoever collected last year.
                        P("@collector", DBNull.Value), P("@history", history),
                    ], ct);
                carried++;
            }

            // Carry every expense category forward too, seeding the new year's
            // forecast from what that category actually cost last year (actual,
            // not the old forecast) — a real starting point instead of a blank
            // budget. contract_amount deliberately resets to 0: a signed
            // contract belongs to the year it was signed for.
            var priorBudget = await QueryAsync(db, "SELECT * FROM v_budget_vs_actual WHERE year_id=@year AND org_id=@org",
                [P("@year", id), Org()], ct);

            foreach (var line in priorBudget)
            {
                await DynamicSql.ExecuteAsync(db, """
                    INSERT INTO budget_lines (id, org_id, year_id, category, notes, forecast, contract_amount, sort_order)
                    VALUES (@id, @org, @year, @category, @notes, @forecast, 0, @sortOrder)
                    """,
                    [
                        P("@id", Guid.NewGuid()), Org(), P("@year", newYearId),
                        P("@category", line["category"] ?? DBNull.Value), P("@notes", line["notes"] ?? DBNull.Value),
                        P("@forecast", line["actual"] is decimal a ? a : 0m),
                        P("@sortOrder", line["sort_order"] ?? DBNull.Value),
                    ], ct);
                budgetLinesCarried++;
            }

            await transaction.CommitAsync(ct);
        });

        var newYear = (await QueryAsync(db, "SELECT * FROM puja_years WHERE id=@id AND org_id=@org", [P("@id", newYearId)], ct)).FirstOrDefault();
        return Results.Ok(new { year = newYear, carried, budgetLinesCarried });
    }
}
