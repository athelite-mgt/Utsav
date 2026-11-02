using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Shared rules for money that waits for Finance. Kept apart from the generic
/// CRUD handlers so the policy reads in one place: who is an approver, which
/// tables are gated, and what a non-approver may touch.
/// </summary>
internal static class Approvals
{
    /// <summary>The caller as the approval rules see them.</summary>
    internal readonly record struct CallerInfo(Guid? UserId, string? Name, AppRole? Role)
    {
        public bool IsApprover => Role is { } r && ApprovalPolicy.CanApprove(r);

        public string Status => Role is { } r ? ApprovalPolicy.StatusFor(r) : ApprovalPolicy.Pending;
    }

    public static CallerInfo CallerFrom(ClaimsPrincipal user) => new(
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null,
        user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value,
        user.CallerRole());

    /// <summary>The income tables whose instalments need approval (Feast stays outside it).</summary>
    public static bool IsIncomeTable(string table) =>
        table is "subscriptions" or "ad_bookings" or "special_donations";

    public static bool IsGated(string table) => IsIncomeTable(table) || table == "expenses";

    /// <summary>Tells the approvers something is waiting for them.</summary>
    public static Task NotifySubmittedAsync(UtsavDbContext db, CallerInfo caller, string what, decimal? amount, CancellationToken ct) =>
        DynamicSql.NotifyAsync(new Infrastructure.Push.PushNotificationRequest(
            db.TenantId, caller.UserId, "Waiting for your approval",
            amount is { } a ? $"{caller.Name ?? "A member"} · {what} — ₹{a:N0}" : $"{caller.Name ?? "A member"} · {what}",
            "/approvals", ActorName: caller.Name, Kind: "approval", Roles: ["admin", "finance"]), ct);

    /// <summary>The only bill fields a role that may merely record payments can change.</summary>
    public static readonly IReadOnlySet<string> SubmitterIncomeColumns =
        new HashSet<string>(StringComparer.Ordinal) { "mode", "collected_on", "comments", "updated_at" };

    /// <summary>Server-stamped columns for a new expense: who recorded it and where it starts.</summary>
    public static IEnumerable<(string Column, object Value)> ExpenseCreateColumns(CallerInfo caller)
    {
        yield return ("status", caller.Status);
        if (caller.UserId is { } id) yield return ("recorded_by_user_id", id);
        if (caller.Name is { } name) yield return ("recorded_by_name", name);
    }

    /// <summary>
    /// A member who cannot approve may only change or withdraw their own
    /// expense while it is still awaiting a decision. Null when that holds;
    /// otherwise the refusal to return.
    /// </summary>
    public static async Task<IResult?> CanChangeOwnOpenExpenseAsync(
        UtsavDbContext db, Guid expenseId, CallerInfo caller, CancellationToken ct)
    {
        var rows = await ModuleEndpoints.QueryAsync(db,
            "SELECT status, recorded_by_user_id FROM expenses WHERE id = @id AND org_id = @org",
            [new SqlParameter("@id", expenseId), new SqlParameter("@org", db.TenantId)], ct);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return Results.Json(new { error = "Not found in this organization" }, statusCode: 404);
        }

        var open = ApprovalPolicy.IsOpen((string?)row["status"] ?? ApprovalPolicy.Approved);
        var mine = caller.UserId is { } uid && row["recorded_by_user_id"] is Guid owner && owner == uid;
        return open && mine
            ? null
            : Results.Json(new { error = "Only Finance or Admin can change an approved entry, or one recorded by someone else" }, statusCode: 403);
    }

    /// <summary>Whether a bill has any instalment that is not rejected — money a non-approver must not delete by deleting the bill.</summary>
    public static async Task<bool> HasLiveInstalmentsAsync(UtsavDbContext db, string table, Guid parentId, CancellationToken ct)
    {
        var parentType = table switch
        {
            "subscriptions" => "subscription",
            "ad_bookings" => "ad_booking",
            _ => "special_donation",
        };
        var rows = await ModuleEndpoints.QueryAsync(db,
            "SELECT COUNT(*) AS n FROM collection_payments WHERE parent_type = @t AND parent_id = @id AND org_id = @org AND status <> 'rejected'",
            [new SqlParameter("@t", parentType), new SqlParameter("@id", parentId), new SqlParameter("@org", db.TenantId)], ct);
        return Convert.ToInt32(rows.First()["n"], System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static string ParentTypeOf(string table) => table switch
    {
        "subscriptions" => "subscription",
        "ad_bookings" => "ad_booking",
        _ => "special_donation",
    };

    private const string PendingSum =
        "(SELECT ISNULL(SUM(amount), 0) FROM collection_payments WHERE parent_type = @t AND parent_id = @id AND org_id = @org AND status IN ('pending', 'sent_back'))";

    /// <summary>
    /// The bill's collected total is the sum of its instalments that have not
    /// been rejected — pending ones count on the bill (so the donor isn't chased
    /// twice) — and <c>pending_collected</c> is the part of it not yet approved.
    /// Reports and the dashboard use <c>collected − pending_collected</c>.
    /// </summary>
    public static Task RecomputeCollectedAsync(UtsavDbContext db, string table, Guid parentId, CancellationToken ct) =>
        RecomputeAsync(db, table, parentId, "collected = (SELECT ISNULL(SUM(amount), 0) FROM collection_payments "
            + "WHERE parent_type = @t AND parent_id = @id AND org_id = @org AND status <> 'rejected'), pending_collected = " + PendingSum, ct);

    /// <summary>Refreshes only the not-yet-approved part, for changes that never alter what the donor paid.</summary>
    public static Task RecomputePendingAsync(UtsavDbContext db, string table, Guid parentId, CancellationToken ct) =>
        RecomputeAsync(db, table, parentId, "pending_collected = " + PendingSum, ct);

    private static Task RecomputeAsync(UtsavDbContext db, string table, Guid parentId, string assignments, CancellationToken ct) =>
        // The table name comes from the whitelist the callers resolved, never from the request.
        DynamicSql.ExecuteAsync(db, $"UPDATE [{table}] SET {assignments} WHERE id = @id AND org_id = @org;",
            [new SqlParameter("@t", ParentTypeOf(table)), new SqlParameter("@id", parentId), new SqlParameter("@org", db.TenantId)], ct);
}
