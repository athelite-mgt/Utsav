using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Generic create/update/delete over the whitelisted tables in
/// <see cref="TableRegistry"/>, ported from the <c>/api/:table</c> routes in
/// <c>server/app.mjs</c>.
///
/// These are the only endpoints that build SQL dynamically. Identifiers can't
/// be parameterised, so they never come from the request: the table name is a
/// dictionary key and every column is checked against that table's whitelist
/// before it is used. Values are always bound as parameters.
/// </summary>
public static class CrudEndpoints
{
    public static IEndpointRouteBuilder MapCrudEndpoints(this IEndpointRouteBuilder app)
    {
        // Baseline only here — guard() in app.mjs was admin-only for every
        // table, but the permission a given table actually needs varies (a
        // Finance member can write expenses, not lookups), and the route
        // shape "/api/{table}" doesn't know which table until the request
        // arrives. Each handler checks TableRegistry.RequiredPermission for
        // the resolved table once it knows it. Mapped last so the specific
        // routes above always win over "{table}".
        var group = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);

        group.MapPost("/{table}", CreateAsync);
        group.MapPatch("/{table}/{id:guid}", UpdateAsync);
        group.MapDelete("/{table}/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        string table, JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        if (!TableRegistry.TryResolve(table, out var safeTable, out var allowedColumns))
        {
            return Results.Json(new { error = "Unknown resource" }, statusCode: 404);
        }
        if (Forbidden(user, safeTable) is { } forbidden)
        {
            return forbidden;
        }

        var supplied = SuppliedColumns(body, allowedColumns);
        var id = Guid.NewGuid();

        // Parameter named after the real column (@category, @amount, …) rather
        // than positionally (@p0, @p1) — c.Column is already validated against
        // the table's whitelist, so this is exactly as safe, and it's what lets
        // DynamicSql.ExtractParameters recover the actual field name for the
        // audit trail. Positional names meant every generic-CRUD edit stored
        // "p0"/"p1" in History instead of "category"/"amount".
        var columnList = string.Join(", ", supplied.Select(c => $"[{c.Column}]"));
        var valueList = string.Join(", ", supplied.Select(c => $"@{c.Column}"));
        var separator = supplied.Count > 0 ? ", " : string.Empty;

        var sql = $"INSERT INTO [{safeTable}] (id, org_id{separator}{columnList}) "
                + $"VALUES (@id, @org{separator}{valueList});";

        var parameters = new List<SqlParameter>
        {
            new("@id", id),
            new("@org", SessionService.DefaultOrgId),
        };
        parameters.AddRange(supplied.Select(c => new SqlParameter($"@{c.Column}", c.Value)));

        await DynamicSql.ExecuteAsync(db, sql, parameters, ct);
        await PersistCollectionPaymentsAsync(db, user, safeTable, id, body, ct);
        return await ReadRowAsync(db, safeTable, id, ct) is { } row
            ? Results.Ok(row)
            : Results.Json(new { error = "Not found in this organization" }, statusCode: 404);
    }

    private static async Task<IResult> UpdateAsync(
        string table, Guid id, JsonElement body, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        if (!TableRegistry.TryResolve(table, out var safeTable, out var allowedColumns))
        {
            return Results.Json(new { error = "Unknown resource" }, statusCode: 404);
        }
        if (Forbidden(user, safeTable) is { } forbidden)
        {
            return forbidden;
        }

        var supplied = SuppliedColumns(body, allowedColumns);
        if (supplied.Count == 0)
        {
            return Results.Json(new { error = "Nothing to update" }, statusCode: 400);
        }

        // Same reasoning as CreateAsync: name each parameter after its real
        // column so the audit trail can tell "category" from "p0".
        var assignments = string.Join(", ", supplied.Select(c => $"[{c.Column}] = @{c.Column}"));
        var sql = $"UPDATE [{safeTable}] SET {assignments} WHERE id = @id AND org_id = @org;";

        var parameters = supplied
            .Select(c => new SqlParameter($"@{c.Column}", c.Value))
            .Append(new SqlParameter("@id", id))
            .Append(new SqlParameter("@org", SessionService.DefaultOrgId))
            .ToList();

        await DynamicSql.ExecuteAsync(db, sql, parameters, ct);
        await PersistCollectionPaymentsAsync(db, user, safeTable, id, body, ct);
        return await ReadRowAsync(db, safeTable, id, ct) is { } row
            ? Results.Ok(row)
            : Results.Json(new { error = "Not found in this organization" }, statusCode: 404);
    }

    private static async Task<IResult> DeleteAsync(
        string table, Guid id, ClaimsPrincipal user, UtsavDbContext db, CancellationToken ct)
    {
        if (!TableRegistry.TryResolve(table, out var safeTable, out _))
        {
            return Results.Json(new { error = "Unknown resource" }, statusCode: 404);
        }
        if (Forbidden(user, safeTable) is { } forbidden)
        {
            return forbidden;
        }

        // Stand in for the ON DELETE SET NULL that SQL Server wouldn't accept.
        if (TableRegistry.DetachBeforeDelete.TryGetValue(safeTable, out var detachments))
        {
            foreach (var (childTable, childColumn) in detachments)
            {
                await DynamicSql.ExecuteAsync(db, $"UPDATE [{childTable}] SET [{childColumn}] = NULL WHERE [{childColumn}] = @id AND org_id = @org;",
                    [new SqlParameter("@id", id), new SqlParameter("@org", SessionService.DefaultOrgId)],
                    ct);
            }
        }

        await DynamicSql.ExecuteAsync(db, $"DELETE FROM [{safeTable}] WHERE id = @id AND org_id = @org;",
            [new SqlParameter("@id", id), new SqlParameter("@org", SessionService.DefaultOrgId)],
            ct);

        return Results.Ok(new { ok = true });
    }

    /// <summary>
    /// Null if the caller may write to this table, an <c>IResult</c> to
    /// return otherwise. Every table in <see cref="TableRegistry.Tables"/> has
    /// an entry in <see cref="TableRegistry.RequiredPermission"/> — a missing
    /// one is treated as "nobody", not "everybody", the same fail-closed
    /// posture as an unparseable role.
    /// </summary>
    private static IResult? Forbidden(ClaimsPrincipal user, string safeTable)
    {
        var permission = TableRegistry.RequiredPermission.GetValueOrDefault(safeTable, (Permission)(-1));
        var role = user.CallerRole();
        return role is { } r && PermissionMatrix.Allows(r, permission)
            ? null
            : Results.Json(new { error = $"Your role can't write to {safeTable}" }, statusCode: 403);
    }

    /// <summary>The three income tables whose collections split into dated part-payments, mapped to their collection_payments.parent_type tag.</summary>
    private static readonly Dictionary<string, string> CollectionParentType = new(StringComparer.Ordinal)
    {
        ["subscriptions"] = "subscription",
        ["ad_bookings"] = "ad_booking",
        ["special_donations"] = "special_donation",
        ["feasts"] = "feast",
    };

    /// <summary>
    /// Replaces a collection record's dated part-payments from the request's
    /// <c>payments</c> array (each {amount, paid_on, mode}). The parent's own
    /// <c>collected</c> column is written by the normal CRUD path above (sent as
    /// the sum by the dialog), so it stays the audited, notified source of
    /// truth; these rows are the "what came in, on what date, how, by whom"
    /// detail the fund position and reports read. "By whom" is always the
    /// signed-in user — never a free-typed collector. A no-op for any table
    /// that isn't one of the three income streams, or a request without a
    /// payments array (so non-dialog callers are unaffected).
    /// </summary>
    private static async Task PersistCollectionPaymentsAsync(
        UtsavDbContext db, ClaimsPrincipal user, string table, Guid parentId, JsonElement body, CancellationToken ct)
    {
        if (!CollectionParentType.TryGetValue(table, out var parentType)) return;
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("payments", out var payments)
            || payments.ValueKind != JsonValueKind.Array) return;
        if (!body.TryGetProperty("year_id", out var yearEl) || !Guid.TryParse(yearEl.GetString(), out var yearId)) return;

        var userId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (Guid?)null;
        var userName = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;

        var existing = await db.CollectionPayments
            .Where(p => p.ParentType == parentType && p.ParentId == parentId)
            .ToListAsync(ct);

        var incoming = new List<SettlementFields>();
        foreach (var p in payments.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var amount = p.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : 0m;
            if (amount == 0m) continue; // skip empty rows the dialog may leave behind

            incoming.Add(new SettlementFields(
                amount,
                p.TryGetProperty("paid_on", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                p.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null));
        }

        var (added, removed) = DiffSettlements(
            existing.Select(e => new SettlementFields(e.Amount, e.PaidOn, e.Mode)).ToList(), incoming);

        db.CollectionPayments.RemoveRange(existing);
        foreach (var s in incoming)
        {
            db.CollectionPayments.Add(new CollectionPayment
            {
                Id = Guid.NewGuid(),
                OrgId = SessionService.DefaultOrgId,
                YearId = yearId,
                ParentType = parentType,
                ParentId = parentId,
                Amount = s.Amount,
                PaidOn = s.PaidOn,
                Mode = s.Mode,
                RecordedByUserId = userId,
                RecordedByName = userName,
            });
        }

        await db.SaveChangesAsync(ct);

        // History never sees these writes otherwise — they go through EF Core
        // directly above, not DynamicSql.ExecuteAsync's audit capture. One
        // entry per added/removed instalment so a settlement reads with its
        // own amount/date/mode, the same granularity as any other change.
        if (added.Count > 0 || removed.Count > 0)
        {
            var label = await ResolveSettlementLabelAsync(db, table, parentId, ct);
            foreach (var s in added)
            {
                await DynamicSql.AuditCollectionPaymentAsync("INSERT INTO", label, s.ToAuditFields(), ct);
            }
            foreach (var s in removed)
            {
                await DynamicSql.AuditCollectionPaymentAsync("DELETE FROM", label, s.ToAuditFields(), ct);
            }
        }
    }

    /// <summary>One instalment's comparable fields — a settlement has no client-round-tripped
    /// id, so added/removed rows are matched by exact (amount, date, mode) equality.</summary>
    internal readonly record struct SettlementFields(decimal Amount, string? PaidOn, string? Mode)
    {
        public Dictionary<string, object?> ToAuditFields() => new()
        {
            ["amount"] = Amount,
            ["paid_on"] = PaidOn,
            ["mode"] = Mode,
        };
    }

    /// <summary>
    /// Matches each incoming instalment against an unconsumed existing one with the
    /// same amount/date/mode; whatever's left over is a real add or remove. An edited
    /// instalment (say, the amount changed) has no stable id to follow, so it shows up
    /// as a remove-of-the-old plus an add-of-the-new rather than a single "changed"
    /// entry — a fair reading for a per-instalment ledger. Pure so it's directly
    /// unit-testable without a database (see CrudEndpointsTests).
    /// </summary>
    internal static (List<SettlementFields> Added, List<SettlementFields> Removed) DiffSettlements(
        IReadOnlyList<SettlementFields> existing, IReadOnlyList<SettlementFields> incoming)
    {
        var remaining = new List<SettlementFields>(existing);
        var added = new List<SettlementFields>();
        foreach (var inc in incoming)
        {
            var idx = remaining.FindIndex(e => e.Amount == inc.Amount && e.PaidOn == inc.PaidOn && e.Mode == inc.Mode);
            if (idx >= 0)
            {
                remaining.RemoveAt(idx);
            }
            else
            {
                added.Add(inc);
            }
        }

        return (added, remaining);
    }

    /// <summary>
    /// A human name for the donor/advertiser/special-donation behind a settlement —
    /// same reasoning as AuditableTables, but resolved explicitly here since the
    /// parent is polymorphic (donor, advertiser, or the row's own name) and can't be
    /// expressed by that map's single FkTable/FkIdColumn shape. Best-effort: any
    /// failure yields null and the audit entry just shows the page without a name.
    /// </summary>
    private static async Task<string?> ResolveSettlementLabelAsync(UtsavDbContext db, string table, Guid parentId, CancellationToken ct)
    {
        var sql = table switch
        {
            "subscriptions" => "SELECT TOP 1 d.name + ISNULL(' ' + d.surname, '') FROM subscriptions s JOIN donors d ON d.id = s.donor_id WHERE s.id = @id AND s.org_id = @org",
            "ad_bookings" => "SELECT TOP 1 a.name FROM ad_bookings ab JOIN advertisers a ON a.id = ab.advertiser_id WHERE ab.id = @id AND ab.org_id = @org",
            "special_donations" => "SELECT TOP 1 name FROM special_donations WHERE id = @id AND org_id = @org",
            "feasts" => "SELECT TOP 1 d.name + ISNULL(' ' + d.surname, '') FROM feasts f JOIN donors d ON d.id = f.donor_id WHERE f.id = @id AND f.org_id = @org",
            _ => null,
        };
        if (sql is null) return null;

        try
        {
            // Through the chokepoint, so the lookup is tenant-bound and
            // guarded like every other read.
            var rows = await ModuleEndpoints.QueryAsync(db, sql, [new SqlParameter("@id", parentId)], ct);
            var result = rows.FirstOrDefault()?.Values.FirstOrDefault();
            return result is null or DBNull ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Body keys narrowed to the table's whitelist. Unknown keys are dropped
    /// silently rather than refused — the Node version filtered the same way,
    /// and the React forms do post fields the server has never stored.
    /// </summary>
    private static List<(string Column, object Value)> SuppliedColumns(
        JsonElement body, System.Collections.Frozen.FrozenSet<string> allowedColumns)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var supplied = new List<(string Column, object Value)>();
        foreach (var property in body.EnumerateObject())
        {
            // Take the whitelist's own instance of the name, never the caller's.
            if (TableRegistry.TryResolveColumn(allowedColumns, property.Name, out var column))
            {
                supplied.Add((column, SqlValue.FromJson(property.Value)));
            }
        }

        return supplied;
    }

    /// <summary>
    /// Reads a row back as a dictionary so the response carries exactly the
    /// columns the table has — including <c>due</c> and any default the
    /// database filled in — matching Node's <c>SELECT *</c> echo.
    /// </summary>
    private static async Task<Dictionary<string, object?>?> ReadRowAsync(
        UtsavDbContext db, string table, Guid id, CancellationToken ct)
    {
        // Through the chokepoint: the tenant is bound and the statement is
        // guarded there, not re-implemented here.
        var rows = await ModuleEndpoints.QueryAsync(
            db, $"SELECT * FROM [{table}] WHERE id = @id AND org_id = @org;", [new SqlParameter("@id", id)], ct);
        return rows.FirstOrDefault();
    }
}
