using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Data;
using static Utsav.Api.Endpoints.AdvanceEndpoints;
using static Utsav.Api.Endpoints.ModuleEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Vendors, contracts and their payment schedules — ported from
/// <c>server/app.mjs</c> lines 516-608. Reads are open to every active
/// member; writes need <see cref="Permission.WriteFinance"/>.
/// </summary>
public static class ContractEndpoints
{
    // The caller passes updated_at explicitly, matching the convention used by
    // subscriptions and ad_bookings (see useEntityMutations), rather than the
    // route reaching for a backend-specific "now".
    private static readonly string[] ContractColumns =
        ["vendor_id", "title", "category", "contract_amount", "start_date", "end_date", "status", "terms", "notes", "updated_at"];

    private static readonly string[] PaymentColumns =
        ["milestone", "amount", "due_date", "paid_date", "status", "note"];

    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var member = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        var write = app.MapGroup("/api").RequirePermission(Permission.WriteFinance);

        member.MapGet("/vendors", (UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, "SELECT * FROM vendors WHERE org_id=@org ORDER BY name", [Org(db)], ct));

        member.MapGet("/contracts", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, "SELECT * FROM v_contract_status WHERE year_id=@year AND org_id=@org ORDER BY created_at DESC",
                [P("@year", year_id), Org(db)], ct));

        write.MapPost("/contracts", CreateContractAsync);
        write.MapPatch("/contracts/{id:guid}", UpdateContractAsync);
        write.MapDelete("/contracts/{id:guid}", DeleteContractAsync);

        member.MapGet("/contracts/{id:guid}/payments", (Guid id, UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, "SELECT * FROM contract_payments WHERE contract_id=@id AND org_id=@org ORDER BY due_date, created_at",
                [P("@id", id), Org(db)], ct));

        write.MapPost("/contract_payments", CreatePaymentAsync);
        write.MapPatch("/contract_payments/{id:guid}", UpdatePaymentAsync);
        write.MapDelete("/contract_payments/{id:guid}", DeletePaymentAsync);

        return app;
    }

    private static async Task<IResult> CreateContractAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!body.TryGetProperty("vendor_id", out var vendorId) || !body.TryGetProperty("year_id", out var yearId)
            || !TryString(body, "title", out var title))
        {
            return Results.Json(new { error = "vendor_id, year_id and title are required" }, statusCode: 400);
        }

        var id = Guid.NewGuid();
        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO contracts (id, org_id, year_id, vendor_id, title, category, contract_amount, start_date, end_date, status, terms, notes)
            VALUES (@id, @org, @year, @vendor, @title, @category, @amount, @start, @end, @status, @terms, @notes)
            """,
            [
                P("@id", id), Org(db), P("@year", yearId.GetGuid()), P("@vendor", vendorId.GetGuid()), P("@title", title),
                P("@category", Opt(body, "category")),
                P("@amount", body.TryGetProperty("contract_amount", out var amount) ? amount.GetDecimal() : 0m),
                P("@start", Opt(body, "start_date")), P("@end", Opt(body, "end_date")),
                P("@status", TryString(body, "status", out var status) ? status : "draft"),
                P("@terms", Opt(body, "terms")), P("@notes", Opt(body, "notes")),
            ], ct);

        return Results.Ok((await ContractStatusAsync(db, id, ct)).FirstOrDefault());
    }

    private static Task<IResult> UpdateContractAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct) =>
        PatchAsync(db, "contracts", ContractColumns, id, body,
            () => ContractStatusAsync(db, id, ct), ct);

    private static async Task<IResult> DeleteContractAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        // Expenses already booked against this contract keep their history;
        // only the contract_id tag is cleared, so nothing is lost. Postgres did
        // this with ON DELETE SET NULL — SQL Server wouldn't accept that FK
        // (see UtsavDbContext.BreakSecondaryCascadePaths), so it happens here.
        await DynamicSql.ExecuteAsync(db, "UPDATE expenses SET contract_id = NULL WHERE contract_id = @id AND org_id = @org",
            [P("@id", id), Org(db)], ct);

        await DynamicSql.ExecuteAsync(db, "DELETE FROM contracts WHERE id=@id AND org_id=@org", [P("@id", id), Org(db)], ct);

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> CreatePaymentAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!body.TryGetProperty("contract_id", out var contractId)
            || !body.TryGetProperty("amount", out var amount) || amount.GetDecimal() == 0)
        {
            return Results.Json(new { error = "contract_id and amount are required" }, statusCode: 400);
        }

        var id = Guid.NewGuid();
        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO contract_payments (id, org_id, contract_id, milestone, amount, due_date, note)
            VALUES (@id, @org, @contract, @milestone, @amount, @due, @note)
            """,
            [
                P("@id", id), Org(db), P("@contract", contractId.GetGuid()), P("@milestone", Opt(body, "milestone")),
                P("@amount", amount.GetDecimal()), P("@due", Opt(body, "due_date")), P("@note", Opt(body, "note")),
            ], ct);

        return Results.Ok((await PaymentAsync(db, id, ct)).FirstOrDefault());
    }

    private static Task<IResult> UpdatePaymentAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct) =>
        PatchAsync(db, "contract_payments", PaymentColumns, id, body, () => PaymentAsync(db, id, ct), ct);

    private static async Task<IResult> DeletePaymentAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        await DynamicSql.ExecuteAsync(db, "DELETE FROM contract_payments WHERE id=@id AND org_id=@org", [P("@id", id), Org(db)], ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> PatchAsync(
        UtsavDbContext db, string table, string[] allowed, Guid id, JsonElement body,
        Func<Task<List<Dictionary<string, object?>>>> read, CancellationToken ct)
    {
        var supplied = body.ValueKind == JsonValueKind.Object
            ? body.EnumerateObject().Where(p => allowed.Contains(p.Name)).ToList()
            : [];

        if (supplied.Count == 0)
        {
            return Results.Json(new { error = "Nothing to update" }, statusCode: 400);
        }

        var assignments = string.Join(", ", supplied.Select((p, i) => $"[{p.Name}] = @p{i}"));
        var parameters = supplied.Select((p, i) => P($"@p{i}", SqlValue.FromJson(p.Value)))
            .Append(P("@id", id)).Append(Org(db)).ToArray();

        await DynamicSql.ExecuteAsync(db, $"UPDATE [{table}] SET {assignments} WHERE id = @id AND org_id = @org;", parameters, ct);

        var row = (await read()).FirstOrDefault();
        return row is null
            ? Results.Json(new { error = "Not found in this organization" }, statusCode: 404)
            : Results.Ok(row);
    }

    private static Task<List<Dictionary<string, object?>>> ContractStatusAsync(UtsavDbContext db, Guid id, CancellationToken ct) =>
        QueryAsync(db, "SELECT * FROM v_contract_status WHERE id=@id AND org_id=@org", [P("@id", id), Org(db)], ct);

    private static Task<List<Dictionary<string, object?>>> PaymentAsync(UtsavDbContext db, Guid id, CancellationToken ct) =>
        QueryAsync(db, "SELECT * FROM contract_payments WHERE id=@id AND org_id=@org", [P("@id", id), Org(db)], ct);
}
