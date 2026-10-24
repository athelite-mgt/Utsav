using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// The year-scoped module reads from <c>server/app.mjs</c> (lines 279-511).
///
/// These return rows shaped for the UI rather than raw entities — nested
/// <c>donor</c>/<c>collector</c>/<c>advertiser</c> objects, joined display
/// names, and in the case of subscriptions a prior-year lookup. They're read
/// through SQL rather than LINQ projections because the shapes and the exact
/// ordering are what the front end depends on, and translating them literally
/// keeps that contract obvious.
/// </summary>
public static class ModuleEndpoints
{
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var member = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        // Workflow/tasks and the puja-day schedule are readable by every role
        // (Viewer included) per PermissionMatrix — only adding a note is a
        // write, gated below to whoever can write Workflow.
        var writeWorkflow = app.MapGroup("/api").RequirePermission(Permission.WriteWorkflow);

        member.MapGet("/subscriptions", SubscriptionsAsync);
        member.MapGet("/feasts", FeastsAsync);
        member.MapGet("/ad_bookings", AdBookingsAsync);
        member.MapGet("/special_donations", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, "SELECT * FROM special_donations WHERE year_id=@year AND org_id=@org ORDER BY created_at", year_id, ct));
        member.MapGet("/offerings", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, "SELECT * FROM offerings WHERE year_id=@year AND org_id=@org ORDER BY created_at", year_id, ct));
        member.MapGet("/expenses", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, """
                SELECT e.*, v.name AS vendor_name
                  FROM expenses e LEFT JOIN vendors v ON v.id = e.vendor_id
                 WHERE e.year_id=@year AND e.org_id=@org
                 ORDER BY e.spent_on DESC, e.created_at DESC
                """, year_id, ct));
        // Feast's own spending — same join shape as /expenses, separate
        // table so it never mixes into the general fund's figures.
        member.MapGet("/feast_expenses", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, """
                SELECT e.*, v.name AS vendor_name
                  FROM feast_expenses e LEFT JOIN vendors v ON v.id = e.vendor_id
                 WHERE e.year_id=@year AND e.org_id=@org
                 ORDER BY e.spent_on DESC, e.created_at DESC
                """, year_id, ct));

        // Budget and cash ledger stay readable by any active member: the
        // Dashboard summarises both, and viewers can see the Dashboard.
        member.MapGet("/budget", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, "SELECT * FROM v_budget_vs_actual WHERE year_id=@year AND org_id=@org ORDER BY sort_order", year_id, ct));
        member.MapGet("/cash_ledger", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, "SELECT * FROM cash_ledger WHERE year_id=@year AND org_id=@org ORDER BY created_at", year_id, ct));
        member.MapGet("/fund_transfers", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, """
                SELECT * FROM fund_transfers WHERE year_id=@year AND org_id=@org
                 ORDER BY COALESCE(transfer_date,'') DESC, created_at DESC
                """, year_id, ct));

        member.MapGet("/puja_schedule", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, """
                SELECT s.*, c.name AS collector_name
                  FROM puja_schedule s LEFT JOIN collectors c ON c.id = s.assignee_collector_id
                 WHERE s.year_id=@year AND s.org_id=@org
                 ORDER BY s.sort_order, s.event_date, s.created_at
                """, year_id, ct));
        member.MapGet("/workflow_items", (Guid year_id, UtsavDbContext db, CancellationToken ct) =>
            RowsAsync(db, """
                SELECT w.*, c.name AS collector_name
                  FROM workflow_items w LEFT JOIN collectors c ON c.id = w.owner_collector_id
                 WHERE w.year_id=@year AND w.org_id=@org
                 ORDER BY w.sort_order, w.due_date, w.created_at
                """, year_id, ct));

        member.MapGet("/workflow_items/{id:guid}/notes", async (Guid id, UtsavDbContext db, CancellationToken ct) =>
            Results.Ok(await QueryAsync(db,
                "SELECT * FROM workflow_notes WHERE workflow_item_id=@id AND org_id=@org ORDER BY created_at",
                [new SqlParameter("@id", id), new SqlParameter("@org", SessionService.DefaultOrgId)], ct)));

        writeWorkflow.MapPost("/workflow_items/{id:guid}/notes", AddWorkflowNoteAsync);

        return app;
    }

    /// <summary>
    /// Last year's row for the same donor is joined in so the collection screen
    /// can show the bill number that household was billed under previously —
    /// what collectors cross-reference against the paper register.
    ///
    /// <paramref name="category"/> selects the Membership or Special Donation
    /// section; omitting it returns both, which is what the dashboard totals
    /// want. The prior-year join is deliberately *not* filtered by category —
    /// a donor moved between sections still has the same paper history.
    /// </summary>
    private static async Task<IResult> SubscriptionsAsync(
        Guid year_id, string? category, UtsavDbContext db, CancellationToken ct)
    {
        var prior = (await QueryAsync(db, """
            SELECT TOP 1 id, year FROM puja_years
             WHERE org_id=@org AND year < (SELECT year FROM puja_years WHERE id=@year)
             ORDER BY year DESC
            """,
            [new SqlParameter("@org", SessionService.DefaultOrgId), new SqlParameter("@year", year_id)], ct))
            .FirstOrDefault();

        var priorYearId = prior?["id"];
        var priorYear = prior?["year"];

        var rows = await QueryAsync(db, """
            SELECT s.*,
                   d.name AS d_name, d.surname AS d_surname, d.contact AS d_contact,
                   d.zone_id AS d_zone_id,
                   c.id AS c_id, c.name AS c_name,
                   p.bill_no AS prior_bill_no, p.billed AS prior_year_billed,
                   p.collected AS prior_year_collected
              FROM subscriptions s
              JOIN donors d ON d.id = s.donor_id
              LEFT JOIN collectors c ON c.id = s.collector_id
              LEFT JOIN subscriptions p ON p.donor_id = s.donor_id AND p.year_id = @prior
             WHERE s.year_id = @year AND s.org_id = @org
               AND (@category IS NULL OR s.category = @category)
             -- Billed rows first, numerically by bill number; not-yet-billed
             -- donors after, alphabetically.
             ORDER BY CASE WHEN s.bill_no IS NULL OR s.bill_no = '' THEN 1 ELSE 0 END ASC,
                      TRY_CAST(NULLIF(s.bill_no,'') AS INT) ASC,
                      d.name ASC
            """,
            [
                new SqlParameter("@prior", priorYearId ?? (object)DBNull.Value),
                new SqlParameter("@year", year_id),
                new SqlParameter("@org", SessionService.DefaultOrgId),
                new SqlParameter("@category", (object?)category ?? DBNull.Value),
            ], ct);

        foreach (var row in rows)
        {
            var history = ParseHistory(row["history"]);
            row["history"] = history;

            // Years imported before the prior-year join existed carry only what
            // rollover stashed in history — fall back to that.
            row["prior_year"] = priorYear ?? Lookup(history, "prior_year");
            row["prior_bill_no"] = row.GetValueOrDefault("prior_bill_no") ?? Lookup(history, "prior_bill_no");

            row["donor"] = new Dictionary<string, object?>
            {
                ["id"] = row.GetValueOrDefault("donor_id"),
                ["name"] = Take(row, "d_name"),
                ["surname"] = Take(row, "d_surname"),
                ["contact"] = Take(row, "d_contact"),
                ["zone_id"] = Take(row, "d_zone_id"),
            };

            var collectorId = Take(row, "c_id");
            var collectorName = Take(row, "c_name");
            row["collector"] = collectorId is null
                ? null
                : new Dictionary<string, object?> { ["id"] = collectorId, ["name"] = collectorName };
        }

        return Results.Ok(rows);
    }

    /// <summary>
    /// Feast collection for a year — same donor/collector join shape as
    /// <see cref="SubscriptionsAsync"/>, minus the prior-year join: feast
    /// collection has no rollover/history concept, so there is no "last
    /// year's bill" to cross-reference.
    /// </summary>
    private static async Task<IResult> FeastsAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await QueryAsync(db, """
            SELECT f.*,
                   d.name AS d_name, d.surname AS d_surname, d.contact AS d_contact,
                   d.zone_id AS d_zone_id,
                   c.id AS c_id, c.name AS c_name
              FROM feasts f
              JOIN donors d ON d.id = f.donor_id
              LEFT JOIN collectors c ON c.id = f.collector_id
             WHERE f.year_id = @year AND f.org_id = @org
             -- Billed rows first, numerically by bill number; not-yet-billed
             -- donors after, alphabetically.
             ORDER BY CASE WHEN f.bill_no IS NULL OR f.bill_no = '' THEN 1 ELSE 0 END ASC,
                      TRY_CAST(NULLIF(f.bill_no,'') AS INT) ASC,
                      d.name ASC
            """,
            [new SqlParameter("@year", year_id), new SqlParameter("@org", SessionService.DefaultOrgId)], ct);

        foreach (var row in rows)
        {
            row["donor"] = new Dictionary<string, object?>
            {
                ["id"] = row.GetValueOrDefault("donor_id"),
                ["name"] = Take(row, "d_name"),
                ["surname"] = Take(row, "d_surname"),
                ["contact"] = Take(row, "d_contact"),
                ["zone_id"] = Take(row, "d_zone_id"),
            };

            var collectorId = Take(row, "c_id");
            var collectorName = Take(row, "c_name");
            row["collector"] = collectorId is null
                ? null
                : new Dictionary<string, object?> { ["id"] = collectorId, ["name"] = collectorName };
        }

        return Results.Ok(rows);
    }

    private static async Task<IResult> AdBookingsAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await QueryAsync(db, """
            SELECT b.*, a.name AS a_name, a.contact AS a_contact
              FROM ad_bookings b JOIN advertisers a ON a.id = b.advertiser_id
             WHERE b.year_id=@year AND b.org_id=@org ORDER BY a.name
            """,
            [new SqlParameter("@year", year_id), new SqlParameter("@org", SessionService.DefaultOrgId)], ct);

        foreach (var row in rows)
        {
            row["advertiser"] = new Dictionary<string, object?>
            {
                ["id"] = row.GetValueOrDefault("advertiser_id"),
                ["name"] = Take(row, "a_name"),
                ["contact"] = Take(row, "a_contact"),
            };
        }

        return Results.Ok(rows);
    }

    /// <summary>
    /// The author is always the signed-in user, never taken from the body, so
    /// the activity log can't be spoofed.
    /// </summary>
    private static async Task<IResult> AddWorkflowNoteAsync(
        Guid id, JsonElement body, HttpContext http, UtsavDbContext db, CancellationToken ct)
    {
        var note = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("note", out var noteText)
            ? noteText.GetString()?.Trim()
            : null;

        if (string.IsNullOrEmpty(note))
        {
            return Results.Json(new { error = "Note text is required" }, statusCode: 400);
        }

        var exists = await QueryAsync(db,
            "SELECT id FROM workflow_items WHERE id=@id AND org_id=@org",
            [new SqlParameter("@id", id), new SqlParameter("@org", SessionService.DefaultOrgId)], ct);
        if (exists.Count == 0)
        {
            return Results.Json(new { error = "Workflow item not found" }, statusCode: 404);
        }

        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        var noteId = Guid.NewGuid();

        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO workflow_notes (id, org_id, workflow_item_id, author_id, author_name, note)
            VALUES (@id, @org, @item, @author, @authorName, @note)
            """,
            [
                new SqlParameter("@id", noteId),
                new SqlParameter("@org", SessionService.DefaultOrgId),
                new SqlParameter("@item", id),
                new SqlParameter("@author", session.User.Id),
                new SqlParameter("@authorName", (object?)(session.User.FullName ?? session.User.Email) ?? DBNull.Value),
                new SqlParameter("@note", note),
            ], ct);

        var created = await QueryAsync(db,
            "SELECT * FROM workflow_notes WHERE id=@id AND org_id=@org",
            [new SqlParameter("@id", noteId), new SqlParameter("@org", SessionService.DefaultOrgId)], ct);

        return Results.Ok(created.FirstOrDefault());
    }

    private static async Task<IResult> RowsAsync(UtsavDbContext db, string sql, Guid yearId, CancellationToken ct) =>
        Results.Ok(await QueryAsync(db, sql,
            [new SqlParameter("@year", yearId), new SqlParameter("@org", SessionService.DefaultOrgId)], ct));

    /// <summary>
    /// Returns rows as plain dictionaries so every column the table has reaches
    /// the client under its own name, the way Express echoed driver rows.
    ///
    /// Must enlist in the ambient transaction when one exists (e.g. rollover's
    /// manually-managed <c>BeginTransactionAsync</c>) — a raw <see cref="SqlCommand"/>
    /// built straight off the connection does not do this automatically, and
    /// SqlClient refuses to run it otherwise: "BeginExecuteReader requires the
    /// command to have a transaction when the connection assigned to the
    /// command is in a pending local transaction." <see cref="CurrentTransaction"/>
    /// is null outside a transaction, which is every other caller of this
    /// method — setting a null Transaction is a no-op for them.
    /// </summary>
    internal static async Task<List<Dictionary<string, object?>>> QueryAsync(
        UtsavDbContext db, string sql, SqlParameter[] parameters, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object? Take(Dictionary<string, object?> row, string key)
    {
        row.Remove(key, out var value);
        return value;
    }

    private static Dictionary<string, JsonElement>? ParseHistory(object? raw)
    {
        if (raw is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? Lookup(Dictionary<string, JsonElement>? history, string key) =>
        history is not null && history.TryGetValue(key, out var value) && value.ValueKind is not JsonValueKind.Null
            ? SqlValue.FromJson(value)
            : null;
}
