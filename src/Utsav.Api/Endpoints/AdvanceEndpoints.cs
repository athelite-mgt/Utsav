using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using static Utsav.Api.Endpoints.ModuleEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Cash advances — a float given to a member before the bill arrives. Settling
/// either books a real expense (<c>kind: 'expense'</c>) or records cash coming
/// back (<c>kind: 'return'</c>); the advance auto-closes once the settlements
/// cover the amount issued. Ported from <c>server/app.mjs</c> lines 616-734.
///
/// Settle and unsettle run in transactions here. The Node originals didn't, so
/// a failure partway through could leave an expense row with no settlement
/// against it — money on the books that no advance accounts for. That was a
/// latent bug worth fixing in the port rather than reproducing.
/// </summary>
public static class AdvanceEndpoints
{
    private static readonly string[] UpdatableColumns =
        ["issued_to_name", "issued_to_collector_id", "amount", "issued_on", "purpose", "issued_by", "mode", "status", "notes"];

    public static IEndpointRouteBuilder MapAdvanceEndpoints(this IEndpointRouteBuilder app)
    {
        // Advances are readable by every role, Viewer included — only issuing,
        // editing and settling them is gated, to whoever can write Advances.
        var member = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        var write = app.MapGroup("/api").RequirePermission(Permission.WriteAdvances);

        member.MapGet("/advances", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, "SELECT * FROM v_advance_balances WHERE year_id=@year AND org_id=@org ORDER BY created_at DESC",
                [P("@year", year_id), Org()], ct));

        write.MapPost("/advances", CreateAsync);
        write.MapPatch("/advances/{id:guid}", UpdateAsync);

        member.MapGet("/advances/{id:guid}/settlements", (Guid id, UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, "SELECT * FROM advance_settlements WHERE advance_id=@id AND org_id=@org ORDER BY created_at",
                [P("@id", id), Org()], ct));

        write.MapPost("/advances/{id:guid}/settle", SettleAsync);
        write.MapDelete("/advance_settlements/{id:guid}", DeleteSettlementAsync);

        // A personal, read-only summary for the Dashboard — not the full
        // Advances page. Any active member can see their own outstanding float
        // without needing write access to the ledger itself.
        member.MapGet("/my/advances", MyAdvancesAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "issued_to_name", out var issuedTo) || !body.TryGetProperty("year_id", out var yearId)
            || !body.TryGetProperty("amount", out var amountElement) || amountElement.GetDecimal() == 0)
        {
            return Results.Json(new { error = "year_id, issued_to_name and amount are required" }, statusCode: 400);
        }

        var id = Guid.NewGuid();
        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO advances (id, org_id, year_id, issued_to_name, issued_to_collector_id, amount, issued_on, purpose, issued_by, mode, notes)
            VALUES (@id, @org, @year, @name, @collector, @amount, @issuedOn, @purpose, @issuedBy, @mode, @notes)
            """,
            [
                P("@id", id), Org(), P("@year", yearId.GetGuid()), P("@name", issuedTo),
                P("@collector", Opt(body, "issued_to_collector_id")), P("@amount", amountElement.GetDecimal()),
                P("@issuedOn", Opt(body, "issued_on")), P("@purpose", Opt(body, "purpose")),
                P("@issuedBy", Opt(body, "issued_by")), P("@mode", Opt(body, "mode")), P("@notes", Opt(body, "notes")),
            ], ct);

        return Results.Ok((await BalanceRowAsync(db, id, ct)).FirstOrDefault());
    }

    private static async Task<IResult> UpdateAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        var supplied = body.ValueKind == JsonValueKind.Object
            ? body.EnumerateObject().Where(p => UpdatableColumns.Contains(p.Name)).ToList()
            : [];

        if (supplied.Count == 0)
        {
            return Results.Json(new { error = "Nothing to update" }, statusCode: 400);
        }

        var assignments = string.Join(", ", supplied.Select((p, i) => $"[{p.Name}] = @p{i}"));
        var parameters = supplied.Select((p, i) => P($"@p{i}", SqlValue.FromJson(p.Value)))
            .Append(P("@id", id)).Append(Org()).ToArray();

        await DynamicSql.ExecuteAsync(db, $"UPDATE advances SET {assignments} WHERE id = @id AND org_id = @org;", parameters, ct);

        var row = (await BalanceRowAsync(db, id, ct)).FirstOrDefault();
        return row is null
            ? Results.Json(new { error = "Not found in this organization" }, statusCode: 404)
            : Results.Ok(row);
    }

    private static async Task<IResult> SettleAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        var advance = (await QueryAsync(db, "SELECT * FROM advances WHERE id=@id AND org_id=@org",
            [P("@id", id), Org()], ct)).FirstOrDefault();
        if (advance is null)
        {
            return Results.Json(new { error = "Advance not found" }, statusCode: 404);
        }

        var amount = body.TryGetProperty("amount", out var amountElement) ? amountElement.GetDecimal() : 0m;
        if (amount <= 0)
        {
            return Results.Json(new { error = "amount must be greater than zero" }, statusCode: 400);
        }

        var kind = TryString(body, "kind", out var k) ? k : null;
        if (kind != "expense" && kind != "return")
        {
            return Results.Json(new { error = "kind must be 'expense' or 'return'" }, statusCode: 400);
        }
        if (kind == "expense" && !TryString(body, "category", out _))
        {
            return Results.Json(new { error = "category is required when settling with a bill" }, statusCode: 400);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            Guid? expenseId = null;
            if (kind == "expense")
            {
                expenseId = Guid.NewGuid();
                await DynamicSql.ExecuteAsync(db, """
                    INSERT INTO expenses (id, org_id, year_id, category, details, amount, spent_on, mode, comment, vendor_id, bill_no, advance_id)
                    VALUES (@id, @org, @year, @category, @details, @amount, @spentOn, @mode, @comment, @vendor, @billNo, @advance)
                    """,
                    [
                        P("@id", expenseId.Value), Org(), P("@year", advance["year_id"]!),
                        P("@category", Opt(body, "category")), P("@details", Opt(body, "details")),
                        P("@amount", amount), P("@spentOn", Opt(body, "spent_on")), P("@mode", Opt(body, "mode")),
                        P("@comment", Opt(body, "comment")), P("@vendor", Opt(body, "vendor_id")),
                        P("@billNo", Opt(body, "bill_no")), P("@advance", id),
                    ], ct);
            }

            await DynamicSql.ExecuteAsync(db, """
                INSERT INTO advance_settlements (id, org_id, advance_id, kind, expense_id, amount, settled_on, note)
                VALUES (@id, @org, @advance, @kind, @expense, @amount, @settledOn, @note)
                """,
                [
                    P("@id", Guid.NewGuid()), Org(), P("@advance", id), P("@kind", kind!),
                    P("@expense", expenseId.HasValue ? expenseId.Value : DBNull.Value),
                    P("@amount", amount), P("@settledOn", Opt(body, "settled_on")), P("@note", Opt(body, "note")),
                ], ct);

            // Auto-close once settlements cover the amount issued. The small
            // epsilon absorbs paise-level rounding.
            await DynamicSql.ExecuteAsync(db, """
                UPDATE advances SET status = 'settled'
                 WHERE id = @id AND org_id = @org AND status = 'open'
                   AND (SELECT outstanding FROM v_advance_balances WHERE id = @id AND org_id = @org) <= 0.01
                """, [P("@id", id), Org()], ct);

            await transaction.CommitAsync(ct);
        });

        return Results.Ok((await BalanceRowAsync(db, id, ct)).FirstOrDefault());
    }

    private static async Task<IResult> DeleteSettlementAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        var settlement = (await QueryAsync(db, "SELECT * FROM advance_settlements WHERE id=@id AND org_id=@org",
            [P("@id", id), Org()], ct)).FirstOrDefault();
        if (settlement is null)
        {
            return Results.Json(new { error = "Not found" }, statusCode: 404);
        }

        var advanceId = (Guid)settlement["advance_id"]!;
        var expenseId = settlement["expense_id"];

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await DynamicSql.ExecuteAsync(db, "DELETE FROM advance_settlements WHERE id=@id AND org_id=@org", [P("@id", id), Org()], ct);

            if (expenseId is Guid expense)
            {
                await DynamicSql.ExecuteAsync(db, "DELETE FROM expenses WHERE id=@id AND org_id=@org", [P("@id", expense), Org()], ct);
            }

            // Reopen the advance if it had auto-closed and is no longer covered.
            await DynamicSql.ExecuteAsync(db, """
                UPDATE advances SET status = 'open'
                 WHERE id = @id AND org_id = @org AND status = 'settled'
                   AND (SELECT outstanding FROM v_advance_balances WHERE id = @id AND org_id = @org) > 0.01
                """, [P("@id", advanceId), Org()], ct);

            await transaction.CommitAsync(ct);
        });

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> MyAdvancesAsync(Guid year_id, HttpContext http, UtsavDbContext db, CancellationToken ct)
    {
        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;

        var collector = (await QueryAsync(db, "SELECT id FROM collectors WHERE user_id=@user AND org_id=@org",
            [P("@user", session.User.Id), Org()], ct)).FirstOrDefault();

        // Not linked to a collector — the Dashboard hides the tile entirely.
        if (collector is null)
        {
            return Results.Ok((object?)null);
        }

        var rows = await QueryAsync(db, """
            SELECT outstanding FROM v_advance_balances
             WHERE issued_to_collector_id=@collector AND year_id=@year AND org_id=@org AND status='open'
            """, [P("@collector", collector["id"]!), P("@year", year_id), Org()], ct);

        return Results.Ok(new
        {
            count = rows.Count,
            outstanding = rows.Sum(r => r["outstanding"] is decimal d ? d : 0m),
        });
    }

    private static Task<List<Dictionary<string, object?>>> BalanceRowAsync(UtsavDbContext db, Guid id, CancellationToken ct) =>
        QueryAsync(db, "SELECT * FROM v_advance_balances WHERE id=@id AND org_id=@org", [P("@id", id), Org()], ct);

    internal static SqlParameter P(string name, object value) => new(name, value);

    internal static SqlParameter Org() => new("@org", SessionService.DefaultOrgId);

    internal static object Opt(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value)
            ? SqlValue.FromJson(value)
            : DBNull.Value;

    internal static bool TryString(JsonElement body, string name, out string value)
    {
        value = string.Empty;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
