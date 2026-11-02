using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Data;
using static Utsav.Api.Endpoints.AdvanceEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Finance's (and Admin's) desk for money that members recorded: one queue, and
/// four things to do with each item — approve (optionally for a smaller amount),
/// send it back with a note, reject it with a reason, and, for claims a member
/// paid from their own pocket, mark them refunded. Every action stamps who and
/// when, and shows in History.
///
/// An item is either <c>income</c> (one instalment of a bill's collection) or an
/// <c>expense</c>; the kind is part of the route so an id can never be guessed
/// into the wrong table.
/// </summary>
public static class ApprovalEndpoints
{
    private const string OpenStatuses = "'pending', 'sent_back'";

    // Both kinds in one shape, so the page is a single list.
    private const string ItemsSql = """
        SELECT 'income' AS kind, cp.id, cp.year_id, py.year AS year, cp.status,
               cp.recorded_by_user_id, cp.recorded_by_name,
               CASE cp.parent_type
                    WHEN 'subscription' THEN LTRIM(RTRIM(COALESCE(d.name,'') + ' ' + COALESCE(d.surname,'')))
                    WHEN 'ad_booking' THEN a.name
                    ELSE sd.name END AS label,
               CASE WHEN cp.parent_type = 'ad_booking' THEN 'Advertisement'
                    WHEN cp.parent_type = 'special_donation' THEN 'Other income'
                    WHEN s.category = 'special' THEN 'Special donation'
                    ELSE 'Membership' END AS source,
               cp.amount, cp.mode, cp.paid_on AS on_date, cp.decision_note AS note, cp.created_at,
               CAST(NULL AS NVARCHAR(20)) AS paid_from
          FROM collection_payments cp
          JOIN puja_years py ON py.id = cp.year_id
          LEFT JOIN subscriptions s ON s.id = cp.parent_id AND cp.parent_type = 'subscription'
          LEFT JOIN donors d ON d.id = s.donor_id
          LEFT JOIN ad_bookings ab ON ab.id = cp.parent_id AND cp.parent_type = 'ad_booking'
          LEFT JOIN advertisers a ON a.id = ab.advertiser_id
          LEFT JOIN special_donations sd ON sd.id = cp.parent_id AND cp.parent_type = 'special_donation'
         WHERE cp.org_id = @org AND cp.parent_type <> 'feast' AND cp.status IN ({0}) {1}
        UNION ALL
        SELECT 'expense', e.id, e.year_id, py.year, e.status, e.recorded_by_user_id, e.recorded_by_name,
               COALESCE(NULLIF(e.details, ''), e.category), COALESCE(e.category, 'Expense'),
               e.amount, e.mode, e.spent_on, e.decision_note, e.created_at, e.paid_from
          FROM expenses e
          JOIN puja_years py ON py.id = e.year_id
         WHERE e.org_id = @org AND e.status IN ({0}) {2}
        ORDER BY created_at
        """;

    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var approve = app.MapGroup("/api").RequirePermission(Permission.ApproveMoney);
        var member = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);

        approve.MapGet("/approvals", ListAsync);
        member.MapGet("/my/approvals", MineAsync);

        approve.MapPost("/approvals/approve_member", ApproveMemberAsync);
        approve.MapPost("/approvals/{kind}/{id:guid}/approve", (string kind, Guid id, JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct) =>
            DecideAsync(kind, id, Decision.Approve, body, user, db, ct));
        approve.MapPost("/approvals/{kind}/{id:guid}/send_back", (string kind, Guid id, JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct) =>
            DecideAsync(kind, id, Decision.SendBack, body, user, db, ct));
        approve.MapPost("/approvals/{kind}/{id:guid}/reject", (string kind, Guid id, JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct) =>
            DecideAsync(kind, id, Decision.Reject, body, user, db, ct));
        approve.MapPost("/expenses/{id:guid}/refund", RefundAsync);

        return app;
    }

    private enum Decision { Approve, SendBack, Reject }

    private static Task<List<Dictionary<string, object?>>> ListAsync(UtsavDbContext db, CancellationToken ct) =>
        ModuleEndpoints.QueryAsync(db, string.Format(ItemsSql, OpenStatuses, "", ""), [P("@org", db.TenantId)], ct);

    /// <summary>The caller's own queue: what is waiting, what was sent back to fix, and what was rejected (with Finance's note).</summary>
    private static async Task<IResult> MineAsync(ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        var caller = Approvals.CallerFrom(user);
        if (caller.UserId is not { } uid)
        {
            return Results.Ok(Array.Empty<object>());
        }

        var sql = string.Format(ItemsSql, OpenStatuses + ", 'rejected'",
            "AND cp.recorded_by_user_id = @uid", "AND e.recorded_by_user_id = @uid");
        return Results.Ok(await ModuleEndpoints.QueryAsync(db, sql, [P("@org", db.TenantId), P("@uid", uid)], ct));
    }

    private static async Task<IResult> DecideAsync(
        string kind, Guid id, Decision decision, JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        if (kind is not ("income" or "expense"))
        {
            return Results.Json(new { error = "kind must be 'income' or 'expense'" }, statusCode: 404);
        }

        var note = TryString(body, "note", out var n) ? n.Trim() : TryString(body, "reason", out var r) ? r.Trim() : null;
        if (decision != Decision.Approve && string.IsNullOrWhiteSpace(note))
        {
            return Results.Json(new { error = decision == Decision.SendBack ? "A note is required when sending an item back" : "A reason is required to reject an item" }, statusCode: 400);
        }

        decimal? amount = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number
            ? a.GetDecimal() : null;
        var caller = Approvals.CallerFrom(user);

        IResult? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            result = kind == "income"
                ? await DecideIncomeAsync(db, caller, id, decision, amount, note, ct)
                : await DecideExpenseAsync(db, caller, id, decision, amount, note, ct);
            if (result is null) await transaction.CommitAsync(ct); else await transaction.RollbackAsync(ct);
        });

        if (result is null && decision != Decision.Approve)
        {
            await NotifyRecorderAsync(db, kind, id, decision, note, caller, ct);
        }

        return result ?? Results.Ok(new { ok = true });
    }

    /// <summary>Tells the member whose entry was sent back or rejected, and why.</summary>
    private static async Task NotifyRecorderAsync(
        UtsavDbContext db, string kind, Guid id, Decision decision, string? note, Approvals.CallerInfo caller, CancellationToken ct)
    {
        var table = kind == "income" ? "collection_payments" : "expenses";
        var rows = await ModuleEndpoints.QueryAsync(db,
            $"SELECT recorded_by_user_id, amount FROM {table} WHERE id = @id AND org_id = @org", [P("@id", id), P("@org", db.TenantId)], ct);
        if (rows.FirstOrDefault() is not { } row || row["recorded_by_user_id"] is not Guid recorder)
        {
            return;
        }

        var amount = row["amount"] is decimal a ? $" — ₹{a:N0}" : "";
        var title = decision == Decision.SendBack ? "Sent back for a fix" : "Not approved";
        await DynamicSql.NotifyAsync(new Infrastructure.Push.PushNotificationRequest(
            db.TenantId, caller.UserId, title, $"{(kind == "income" ? "Payment" : "Expense")}{amount}: {note}", "/approvals",
            ActorName: caller.Name, Kind: "approval", OnlyToUserId: recorder), ct);
    }

    private static string Verb(Decision d) => d switch
    {
        Decision.Approve => ApprovalPolicy.Approved,
        Decision.SendBack => ApprovalPolicy.SentBack,
        _ => ApprovalPolicy.Rejected,
    };

    private static IResult? ValidateAmount(decimal? amount, decimal recorded) =>
        amount is { } a && (a <= 0 || a > recorded)
            ? Results.Json(new { error = $"The approved amount must be more than 0 and no more than the {recorded:0.##} recorded" }, statusCode: 400)
            : null;

    /// <summary>Null on success, otherwise the refusal (the transaction is rolled back).</summary>
    private static async Task<IResult?> DecideIncomeAsync(
        UtsavDbContext db, Approvals.CallerInfo caller, Guid id, Decision decision, decimal? amount, string? note, CancellationToken ct)
    {
        var row = (await ModuleEndpoints.QueryAsync(db,
            "SELECT * FROM collection_payments WHERE id = @id AND org_id = @org AND parent_type <> 'feast'",
            [P("@id", id), P("@org", db.TenantId)], ct)).FirstOrDefault();
        if (row is null)
        {
            return Results.Json(new { error = "Not found in this organization" }, statusCode: 404);
        }

        var status = (string)row["status"]!;
        var allowed = decision == Decision.Reject ? ApprovalPolicy.IsOpen(status) : status == ApprovalPolicy.Pending;
        if (!allowed)
        {
            return Results.Json(new { error = $"This payment is already {status.Replace('_', ' ')}" }, statusCode: 409);
        }

        var recorded = (decimal)row["amount"]!;
        var parentId = (Guid)row["parent_id"]!;
        var parentType = (string)row["parent_type"]!;
        var approved = recorded;

        if (decision == Decision.Approve)
        {
            if (ValidateAmount(amount, recorded) is { } invalid) return invalid;
            approved = amount ?? recorded;
            if (approved < recorded)
            {
                // The member handed over less than they recorded: what arrived is
                // approved, and the rest stays held by them until it does.
                await DynamicSql.ExecuteAsync(db, """
                    INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode,
                                                     recorded_by_user_id, recorded_by_name, status)
                    VALUES (@nid, @org, @year, @pt, @parent, @amount, @paidOn, @mode, @byId, @byName, 'pending')
                    """,
                    [
                        P("@nid", Guid.NewGuid()), P("@org", db.TenantId), P("@year", row["year_id"]!), P("@pt", parentType),
                        P("@parent", parentId), P("@amount", recorded - approved), P("@paidOn", row["paid_on"] ?? DBNull.Value),
                        P("@mode", row["mode"] ?? DBNull.Value), P("@byId", row["recorded_by_user_id"] ?? DBNull.Value),
                        P("@byName", row["recorded_by_name"] ?? DBNull.Value),
                    ], ct);
            }
        }

        await DynamicSql.ExecuteAsync(db, """
            UPDATE collection_payments
               SET status = @status, amount = @amount, decided_by_user_id = @by, decided_by_name = @byName,
                   decided_at = SYSUTCDATETIME(), decision_note = @note
             WHERE id = @id AND org_id = @org
            """,
            [
                P("@status", Verb(decision)), P("@amount", approved), P("@by", (object?)caller.UserId ?? DBNull.Value),
                P("@byName", (object?)caller.Name ?? DBNull.Value), P("@note", (object?)note ?? DBNull.Value),
                P("@id", id), P("@org", db.TenantId),
            ], ct);

        var parentTable = parentType switch { "subscription" => "subscriptions", "ad_booking" => "ad_bookings", _ => "special_donations" };
        if (decision == Decision.Reject)
        {
            // The donor's bill goes back to unpaid by exactly this amount.
            await Approvals.RecomputeCollectedAsync(db, parentTable, parentId, ct);
        }
        else
        {
            await Approvals.RecomputePendingAsync(db, parentTable, parentId, ct);
        }

        var label = await CrudEndpoints.ResolveSettlementLabelAsync(db, parentTable, parentId, ct);
        await DynamicSql.AuditCollectionPaymentAsync("UPDATE", label, new Dictionary<string, object?>
        {
            ["decision"] = Verb(decision), ["amount"] = approved, ["recorded"] = recorded, ["note"] = note,
            ["decided_by"] = caller.Name,
        }, db.TenantId, ct);
        return null;
    }

    private static async Task<IResult?> DecideExpenseAsync(
        UtsavDbContext db, Approvals.CallerInfo caller, Guid id, Decision decision, decimal? amount, string? note, CancellationToken ct)
    {
        var row = (await ModuleEndpoints.QueryAsync(db,
            "SELECT * FROM expenses WHERE id = @id AND org_id = @org", [P("@id", id), P("@org", db.TenantId)], ct)).FirstOrDefault();
        if (row is null)
        {
            return Results.Json(new { error = "Not found in this organization" }, statusCode: 404);
        }

        var status = (string)row["status"]!;
        var allowed = decision == Decision.Reject ? ApprovalPolicy.IsOpen(status) : status == ApprovalPolicy.Pending;
        if (!allowed)
        {
            return Results.Json(new { error = $"This expense is already {status.Replace('_', ' ')}" }, statusCode: 409);
        }

        var claimed = (decimal)row["amount"]!;
        var approved = claimed;
        if (decision == Decision.Approve)
        {
            if (ValidateAmount(amount, claimed) is { } invalid) return invalid;
            approved = amount ?? claimed;
        }

        await DynamicSql.ExecuteAsync(db, """
            UPDATE expenses
               SET status = @status, amount = @amount, decided_by_user_id = @by, decided_by_name = @byName,
                   decided_at = SYSUTCDATETIME(), decision_note = @note
             WHERE id = @id AND org_id = @org
            """,
            [
                P("@status", Verb(decision)), P("@amount", approved), P("@by", (object?)caller.UserId ?? DBNull.Value),
                P("@byName", (object?)caller.Name ?? DBNull.Value), P("@note", (object?)note ?? DBNull.Value),
                P("@id", id), P("@org", db.TenantId),
            ], ct);

        if (row["advance_id"] is Guid advanceId)
        {
            if (decision == Decision.Reject)
            {
                // The bill never happened: the settlement goes, the advance stays out.
                await DynamicSql.ExecuteAsync(db, "DELETE FROM advance_settlements WHERE expense_id = @id AND org_id = @org",
                    [P("@id", id), P("@org", db.TenantId)], ct);
            }
            else if (decision == Decision.Approve)
            {
                // Approved for less: only that much of the advance is accounted for;
                // the difference is still with the member.
                await DynamicSql.ExecuteAsync(db, "UPDATE advance_settlements SET amount = @amount WHERE expense_id = @id AND org_id = @org",
                    [P("@amount", approved), P("@id", id), P("@org", db.TenantId)], ct);
            }

            await ReevaluateAsync(db, advanceId, ct);
        }

        return null;
    }

    /// <summary>Approves every pending item one member recorded — the usual end of a handover.</summary>
    private static async Task<IResult> ApproveMemberAsync(JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "name", out var name))
        {
            return Results.Json(new { error = "name is required" }, statusCode: 400);
        }

        var caller = Approvals.CallerFrom(user);
        var items = (await ModuleEndpoints.QueryAsync(db,
            string.Format(ItemsSql, "'pending'", "AND cp.recorded_by_name = @name", "AND e.recorded_by_name = @name"),
            [P("@org", db.TenantId), P("@name", name)], ct));

        var approved = 0;
        var total = 0m;
        foreach (var item in items)
        {
            var kind = (string)item["kind"]!;
            IResult? failure = null;
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                failure = kind == "income"
                    ? await DecideIncomeAsync(db, caller, (Guid)item["id"]!, Decision.Approve, null, null, ct)
                    : await DecideExpenseAsync(db, caller, (Guid)item["id"]!, Decision.Approve, null, null, ct);
                if (failure is null) await transaction.CommitAsync(ct); else await transaction.RollbackAsync(ct);
            });

            if (failure is null)
            {
                approved++;
                total += (decimal)item["amount"]!;
            }
        }

        return Results.Ok(new { approved, total });
    }

    /// <summary>The committee pays a member back for an approved own-pocket claim.</summary>
    private static async Task<IResult> RefundAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "refunded_on", out var on) || !TryString(body, "mode", out var mode))
        {
            return Results.Json(new { error = "refunded_on and mode are required" }, statusCode: 400);
        }

        var changed = await DynamicSql.ExecuteAsync(db, """
            UPDATE expenses SET refunded_on = @on, refund_mode = @mode
             WHERE id = @id AND org_id = @org AND status = 'approved' AND paid_from = 'own_pocket' AND refunded_on IS NULL
            """, [P("@on", on), P("@mode", mode), P("@id", id), P("@org", db.TenantId)], ct);

        return changed == 0
            ? Results.Json(new { error = "Only an approved own-pocket claim that has not been refunded can be refunded" }, statusCode: 409)
            : Results.Ok(new { ok = true });
    }
}
