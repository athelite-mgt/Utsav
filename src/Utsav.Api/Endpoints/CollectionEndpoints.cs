using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Reads the dated part-payments behind a collection record, so the income
/// dialogs can show and edit the instalment breakdown. Writes go through the
/// normal CRUD save (see <see cref="CrudEndpoints.PersistCollectionPaymentsAsync"/>),
/// which keeps the parent's audited <c>collected</c> total in step.
/// </summary>
public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collection_payments")
            .RequireAuthorization(AuthorizationPolicies.ActiveMember);
        group.MapGet("", ListAsync);
        group.MapGet("/by_stream", ByStreamAsync);
        group.MapGet("/by_zone", ByZoneAsync);
        group.MapGet("/by_date", ByDateAsync);
        group.MapGet("/by_mode", ByModeAsync);
        group.MapGet("/for_year", ForYearAsync);

        return app;
    }

    /// <summary>
    /// Every dated instalment for a year (optionally narrowed to one
    /// <c>parent_type</c>), raw — not summed. Membership/Special Donation's
    /// list-page date-range filter needs this: each subscription only carries
    /// its *latest* instalment's date in <c>collected_on</c> (see
    /// <see cref="Subscription"/>'s doc comment), so filtering the list by
    /// one date against that single column silently hid a donor whose
    /// matching instalment wasn't the most recent one. Fetching every
    /// instalment lets the page check "did *any* payment for this donor
    /// land in the chosen range" instead — <c>mode</c> rides along for the
    /// same reason, so a Mode filter can check every instalment too, not
    /// just the parent's single latest-instalment mode.
    /// </summary>
    private static async Task<IResult> ForYearAsync(Guid year_id, string? parent_type, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await db.CollectionPayments.AsNoTracking()
            .Where(p => p.OrgId == SessionService.DefaultOrgId && p.YearId == year_id
                && (parent_type == null || p.ParentType == parent_type))
            .Select(p => new { parent_id = p.ParentId, amount = p.Amount, paid_on = p.PaidOn, mode = p.Mode })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    /// <summary>
    /// Collections for a year, in a date range, grouped by which income stream
    /// they belong to (membership / special / ad / other — same split as
    /// <see cref="AuditReportEndpoint"/>, since <c>parent_type="subscription"</c>
    /// alone doesn't distinguish membership from special donation; that needs
    /// the subscription's own <c>category</c>). The Reports page's date-filtered
    /// "Collection Summary" and "Income vs Expense" views read the *collected*
    /// figure from here rather than from each subscription/booking's own single
    /// <c>collected</c>/<c>collected_on</c> — those only carry the latest
    /// instalment's date, so gating a donor's whole total on one date silently
    /// mis-bucketed (or dropped) any instalment paid on a different day. Billed
    /// and due are stock figures, not events, so they aren't date-scoped and
    /// stay read from the parent rows as before.
    /// </summary>
    private static async Task<IResult> ByStreamAsync(Guid year_id, string? from, string? to, UtsavDbContext db, CancellationToken ct)
    {
        var sql = $"""
            SELECT CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                        WHEN cp.parent_type='ad_booking' THEN 'ad'
                        WHEN cp.parent_type='feast' THEN 'feast'
                        ELSE 'other' END AS stream,
                   SUM(cp.amount) AS collected
              FROM collection_payments cp
              LEFT JOIN subscriptions s ON s.id = cp.parent_id AND cp.parent_type = 'subscription'
             WHERE cp.year_id = @year AND cp.org_id = @org
               {(string.IsNullOrEmpty(from) ? "" : "AND cp.paid_on >= @from")}
               {(string.IsNullOrEmpty(to) ? "" : "AND cp.paid_on <= @to")}
             GROUP BY CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                           WHEN cp.parent_type='ad_booking' THEN 'ad'
                           WHEN cp.parent_type='feast' THEN 'feast'
                           ELSE 'other' END
            """;

        var parameters = new List<SqlParameter> { new("@year", year_id), new("@org", SessionService.DefaultOrgId) };
        if (!string.IsNullOrEmpty(from)) parameters.Add(new SqlParameter("@from", from));
        if (!string.IsNullOrEmpty(to)) parameters.Add(new SqlParameter("@to", to));

        var rows = (await ModuleEndpoints.QueryAsync(db, sql, [.. parameters], ct))
            .Select(r => new { stream = (string)r["stream"]!, collected = r["collected"] is decimal d ? d : 0m });

        return Results.Ok(rows);
    }

    /// <summary>
    /// Membership/Special Donation collections for a year, in a date range,
    /// grouped by the donor's zone — the same date-scoping fix as
    /// <see cref="ByStreamAsync"/>, applied to the Zone-wise Collection
    /// report. Only <c>parent_type = "subscription"</c> carries a zone
    /// (via its donor); ad bookings and other incomes aren't zoned.
    /// </summary>
    private static async Task<IResult> ByZoneAsync(Guid year_id, string? from, string? to, UtsavDbContext db, CancellationToken ct)
    {
        var sql = $"""
            SELECT COALESCE(z.name, 'No zone') AS zone_name,
                   SUM(cp.amount) AS collected,
                   COUNT(DISTINCT cp.parent_id) AS donors
              FROM collection_payments cp
              JOIN subscriptions s ON s.id = cp.parent_id
              JOIN donors d ON d.id = s.donor_id
              LEFT JOIN zones z ON z.id = d.zone_id
             WHERE cp.parent_type = 'subscription' AND cp.year_id = @year AND cp.org_id = @org
               {(string.IsNullOrEmpty(from) ? "" : "AND cp.paid_on >= @from")}
               {(string.IsNullOrEmpty(to) ? "" : "AND cp.paid_on <= @to")}
             GROUP BY COALESCE(z.name, 'No zone')
            """;

        var parameters = new List<SqlParameter> { new("@year", year_id), new("@org", SessionService.DefaultOrgId) };
        if (!string.IsNullOrEmpty(from)) parameters.Add(new SqlParameter("@from", from));
        if (!string.IsNullOrEmpty(to)) parameters.Add(new SqlParameter("@to", to));

        var rows = (await ModuleEndpoints.QueryAsync(db, sql, [.. parameters], ct))
            .Select(r => new
            {
                zone_name = (string)r["zone_name"]!,
                collected = r["collected"] is decimal d ? d : 0m,
                donors = r["donors"] is int i ? i : Convert.ToInt32(r["donors"]),
            });

        return Results.Ok(rows);
    }

    /// <summary>
    /// Every collection instalment for a year, in a date range, grouped by
    /// calendar day and income stream — the day-wise "Collection by Date"
    /// report. Same stream classification as <see cref="ByStreamAsync"/>.
    /// </summary>
    private static async Task<IResult> ByDateAsync(Guid year_id, string? from, string? to, UtsavDbContext db, CancellationToken ct)
    {
        var sql = $"""
            SELECT cp.paid_on,
                   CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                        WHEN cp.parent_type='ad_booking' THEN 'ad'
                        WHEN cp.parent_type='feast' THEN 'feast'
                        ELSE 'other' END AS stream,
                   SUM(cp.amount) AS collected
              FROM collection_payments cp
              LEFT JOIN subscriptions s ON s.id = cp.parent_id AND cp.parent_type = 'subscription'
             WHERE cp.year_id = @year AND cp.org_id = @org AND cp.paid_on IS NOT NULL
               {(string.IsNullOrEmpty(from) ? "" : "AND cp.paid_on >= @from")}
               {(string.IsNullOrEmpty(to) ? "" : "AND cp.paid_on <= @to")}
             GROUP BY cp.paid_on,
                      CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                           WHEN cp.parent_type='ad_booking' THEN 'ad'
                           WHEN cp.parent_type='feast' THEN 'feast'
                           ELSE 'other' END
             ORDER BY cp.paid_on
            """;

        var parameters = new List<SqlParameter> { new("@year", year_id), new("@org", SessionService.DefaultOrgId) };
        if (!string.IsNullOrEmpty(from)) parameters.Add(new SqlParameter("@from", from));
        if (!string.IsNullOrEmpty(to)) parameters.Add(new SqlParameter("@to", to));

        var rows = (await ModuleEndpoints.QueryAsync(db, sql, [.. parameters], ct))
            .Select(r => new
            {
                paid_on = (string)r["paid_on"]!,
                stream = (string)r["stream"]!,
                collected = r["collected"] is decimal d ? d : 0m,
            });

        return Results.Ok(rows);
    }

    /// <summary>
    /// Every collection instalment for a year, in a date range, grouped by
    /// payment mode and income stream — the "Collection by Type" report.
    /// A blank/null mode is folded into "Unspecified" rather than dropped,
    /// so the report's total always reconciles with Collection Summary's.
    /// </summary>
    private static async Task<IResult> ByModeAsync(Guid year_id, string? from, string? to, UtsavDbContext db, CancellationToken ct)
    {
        var sql = $"""
            SELECT COALESCE(NULLIF(cp.mode, ''), 'Unspecified') AS mode,
                   CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                        WHEN cp.parent_type='ad_booking' THEN 'ad'
                        WHEN cp.parent_type='feast' THEN 'feast'
                        ELSE 'other' END AS stream,
                   SUM(cp.amount) AS collected
              FROM collection_payments cp
              LEFT JOIN subscriptions s ON s.id = cp.parent_id AND cp.parent_type = 'subscription'
             WHERE cp.year_id = @year AND cp.org_id = @org
               {(string.IsNullOrEmpty(from) ? "" : "AND cp.paid_on >= @from")}
               {(string.IsNullOrEmpty(to) ? "" : "AND cp.paid_on <= @to")}
             GROUP BY COALESCE(NULLIF(cp.mode, ''), 'Unspecified'),
                      CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                           WHEN cp.parent_type='ad_booking' THEN 'ad'
                           WHEN cp.parent_type='feast' THEN 'feast'
                           ELSE 'other' END
            """;

        var parameters = new List<SqlParameter> { new("@year", year_id), new("@org", SessionService.DefaultOrgId) };
        if (!string.IsNullOrEmpty(from)) parameters.Add(new SqlParameter("@from", from));
        if (!string.IsNullOrEmpty(to)) parameters.Add(new SqlParameter("@to", to));

        var rows = (await ModuleEndpoints.QueryAsync(db, sql, [.. parameters], ct))
            .Select(r => new
            {
                mode = (string)r["mode"]!,
                stream = (string)r["stream"]!,
                collected = r["collected"] is decimal d ? d : 0m,
            });

        return Results.Ok(rows);
    }

    private static async Task<IResult> ListAsync(string parent_type, Guid parent_id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await db.CollectionPayments.AsNoTracking()
            .Where(p => p.OrgId == SessionService.DefaultOrgId && p.ParentType == parent_type && p.ParentId == parent_id)
            .OrderBy(p => p.PaidOn)
            .Select(p => new
            {
                id = p.Id,
                amount = p.Amount,
                paid_on = p.PaidOn,
                mode = p.Mode,
                recorded_by_name = p.RecordedByName,
            })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }
}
