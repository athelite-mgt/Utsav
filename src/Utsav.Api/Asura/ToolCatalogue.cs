using System.Collections.Frozen;

namespace Utsav.Api.Asura;

internal sealed record AsuraParam(string Name, string Type, bool Required, string Description);

/// <summary>
/// How a result row expands when somebody clicks it: which report to run next,
/// and which column of the clicked row supplies its parameter.
/// </summary>
internal sealed record AsuraDrill(string ToolId, string ParamName, string FromColumn);

internal sealed record AsuraTool(
    string Id,
    string Title,
    string Question,
    string Sql,
    IReadOnlyList<AsuraParam> Parameters,
    string Chart,
    string? LabelColumn,
    IReadOnlyList<string> ValueColumns,
    AsuraDrill? Drill = null);

/// <summary>
/// The reviewed reports Asura can run. This is the fast path and, while
/// <see cref="AsuraOptions.AllowFreeformSql"/> stays false, the <i>only</i>
/// path — the model's job is to choose a report and its parameters, not to
/// write SQL. Every statement here was written by hand, read by a human, and
/// executed against the real schema.
///
/// <para>
/// Each report resolves the puja year the same way: a CTE that takes
/// <c>@yearId</c> when given one and falls back to whichever year is flagged
/// active. That keeps "this year" meaning the same thing in every report
/// without the caller having to know the active year's id, and it is the
/// shape the strongest benchmarked model reached for unprompted.
/// </para>
///
/// <para>
/// <c>ToolCatalogueTests</c> asserts that every statement here passes
/// <see cref="SqlGuard"/> and that every <see cref="AsuraDrill"/> points at a
/// report that exists — so a tool that could not run, or a dead drill link, is
/// a failing test rather than a runtime surprise for a treasurer.
/// </para>
/// </summary>
internal static class ToolCatalogue
{
    /// <summary>
    /// Resolves the reporting year once, shared by every report below.
    /// <c>@yearId</c> is declared on each tool that uses it, and is always
    /// passed (as null when unspecified) so the parameter is bound.
    /// </summary>
    private const string Year = """
        WITH y AS (
            SELECT id FROM puja_years
            WHERE org_id = @orgId
              AND (id = @yearId OR (@yearId IS NULL AND is_active = 1))
        )
        """;

    private static readonly AsuraParam YearParam =
        new("yearId", "guid", false, "Puja year; omit for the active year.");

    /// <summary>
    /// Every date-ish column in this schema is <c>nvarchar</c>, not <c>date</c> —
    /// <c>subscriptions.billed_on</c>, <c>expenses.spent_on</c>,
    /// <c>advances.issued_on</c> and the rest all store text, a legacy of the
    /// SQLite → Postgres → Azure SQL migration path.
    ///
    /// <para>
    /// So anything date-shaped goes through <c>TRY_CONVERT(date, …)</c>. Two
    /// reasons, both learned the hard way here: passing the raw column to
    /// <c>FORMAT</c> fails outright ("Argument data type nvarchar is invalid for
    /// argument 1"), and relying on SQL Server's implicit string-to-date
    /// conversion throws on the first unparseable value in the column. The
    /// implicit version appears to work only while the data is NULL or clean,
    /// which is exactly the sort of thing that breaks in production and not in
    /// a test. <c>TRY_CONVERT</c> yields NULL for a bad value instead of
    /// failing the whole report.
    /// </para>
    /// </summary>
    private const string CollectedDate = "TRY_CONVERT(date, s.collected_on)";

    public static readonly FrozenDictionary<string, AsuraTool> Tools = new[]
    {
        // ---------------- income ----------------------------------------
        new AsuraTool(
            Id: "collections_by_zone",
            Title: "Collections by zone",
            Question: "How is each zone doing on chanda collection? Which zones are behind?",
            Sql: $"""
                {Year}
                SELECT z.id AS zone_id,
                       z.name AS zone,
                       COUNT(s.id) AS donors,
                       ISNULL(SUM(s.projected), 0) AS projected,
                       ISNULL(SUM(s.billed), 0) AS billed,
                       ISNULL(SUM(s.collected), 0) AS collected,
                       ISNULL(SUM(s.billed), 0) - ISNULL(SUM(s.collected), 0) AS outstanding,
                       CASE WHEN ISNULL(SUM(s.projected), 0) = 0 THEN 0
                            ELSE CAST(100.0 * SUM(s.collected) / SUM(s.projected) AS DECIMAL(6,2))
                       END AS pct_of_projection
                FROM zones z
                LEFT JOIN donors d ON d.zone_id = z.id AND d.org_id = @orgId
                LEFT JOIN subscriptions s ON s.donor_id = d.id AND s.org_id = @orgId
                     AND s.year_id IN (SELECT id FROM y) AND s.not_eligible = 0
                WHERE z.org_id = @orgId
                GROUP BY z.id, z.name, z.sort_order
                ORDER BY pct_of_projection ASC, z.sort_order
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "zone",
            ValueColumns: ["projected", "collected"],
            Drill: new AsuraDrill("zone_donor_detail", "zoneId", "zone_id")),

        new AsuraTool(
            Id: "zone_donor_detail",
            Title: "Donors in a zone",
            Question: "Show the individual donors and amounts behind one zone's total.",
            Sql: $"""
                {Year}
                SELECT d.id AS donor_id,
                       d.name + ISNULL(' ' + d.surname, '') AS donor,
                       d.contact,
                       c.name AS collector,
                       s.category,
                       ISNULL(s.projected, 0) AS projected,
                       ISNULL(s.billed, 0) AS billed,
                       ISNULL(s.collected, 0) AS collected,
                       ISNULL(s.billed, 0) - ISNULL(s.collected, 0) AS outstanding,
                       s.collected_on
                FROM donors d
                LEFT JOIN subscriptions s ON s.donor_id = d.id AND s.org_id = @orgId
                     AND s.year_id IN (SELECT id FROM y)
                LEFT JOIN collectors c ON c.id = s.collector_id AND c.org_id = @orgId
                WHERE d.org_id = @orgId AND d.zone_id = @zoneId
                ORDER BY outstanding DESC, donor
                """,
            Parameters: [YearParam, new("zoneId", "guid", true, "The zone to expand.")],
            Chart: "none",
            LabelColumn: "donor",
            ValueColumns: ["collected", "outstanding"]),

        new AsuraTool(
            Id: "collections_by_collector",
            Title: "Collector performance",
            Question: "Which collectors have brought in the most, and who is lagging?",
            Sql: $"""
                {Year}
                SELECT c.id AS collector_id,
                       c.name AS collector,
                       COUNT(s.id) AS donors,
                       ISNULL(SUM(s.billed), 0) AS billed,
                       ISNULL(SUM(s.collected), 0) AS collected,
                       ISNULL(SUM(s.billed), 0) - ISNULL(SUM(s.collected), 0) AS outstanding
                FROM collectors c
                LEFT JOIN subscriptions s ON s.collector_id = c.id AND s.org_id = @orgId
                     AND s.year_id IN (SELECT id FROM y) AND s.not_eligible = 0
                WHERE c.org_id = @orgId
                GROUP BY c.id, c.name, c.sort_order
                ORDER BY collected DESC
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "collector",
            ValueColumns: ["collected", "outstanding"],
            Drill: new AsuraDrill("collector_donor_detail", "collectorId", "collector_id")),

        new AsuraTool(
            Id: "collector_donor_detail",
            Title: "Donors for a collector",
            Question: "Which donors is one collector responsible for, and what is outstanding?",
            Sql: $"""
                {Year}
                SELECT d.name + ISNULL(' ' + d.surname, '') AS donor,
                       z.name AS zone,
                       d.contact,
                       ISNULL(s.billed, 0) AS billed,
                       ISNULL(s.collected, 0) AS collected,
                       ISNULL(s.billed, 0) - ISNULL(s.collected, 0) AS outstanding,
                       s.collected_on
                FROM subscriptions s
                JOIN donors d ON d.id = s.donor_id AND d.org_id = @orgId
                LEFT JOIN zones z ON z.id = d.zone_id AND z.org_id = @orgId
                WHERE s.org_id = @orgId AND s.collector_id = @collectorId
                  AND s.year_id IN (SELECT id FROM y)
                ORDER BY outstanding DESC, donor
                """,
            Parameters: [YearParam, new("collectorId", "guid", true, "The collector to expand.")],
            Chart: "none",
            LabelColumn: "donor",
            ValueColumns: ["collected", "outstanding"]),

        new AsuraTool(
            Id: "donor_dues",
            Title: "Donors with money outstanding",
            Question: "Who has been billed but hasn't paid yet, and how much is owed?",
            Sql: $"""
                {Year}
                SELECT d.name + ISNULL(' ' + d.surname, '') AS donor,
                       z.name AS zone,
                       d.contact,
                       c.name AS collector,
                       s.bill_no,
                       s.billed_on,
                       ISNULL(s.billed, 0) AS billed,
                       ISNULL(s.collected, 0) AS collected,
                       ISNULL(s.billed, 0) - ISNULL(s.collected, 0) AS outstanding,
                       DATEDIFF(day, TRY_CONVERT(date, s.billed_on), GETDATE()) AS days_since_billed
                FROM subscriptions s
                JOIN donors d ON d.id = s.donor_id AND d.org_id = @orgId
                LEFT JOIN zones z ON z.id = d.zone_id AND z.org_id = @orgId
                LEFT JOIN collectors c ON c.id = s.collector_id AND c.org_id = @orgId
                WHERE s.org_id = @orgId
                  AND s.year_id IN (SELECT id FROM y)
                  AND s.not_eligible = 0
                  AND ISNULL(s.billed, 0) > ISNULL(s.collected, 0)
                ORDER BY outstanding DESC
                """,
            Parameters: [YearParam],
            Chart: "none",
            LabelColumn: "donor",
            ValueColumns: ["outstanding"]),

        new AsuraTool(
            Id: "income_mix",
            Title: "Where the money came from",
            Question: "What is the split of income across chanda, ads, special donations and offerings?",
            Sql: $"""
                {Year}
                SELECT 'Chanda (membership)' AS source,
                       ISNULL((SELECT SUM(collected) FROM subscriptions
                               WHERE org_id = @orgId AND year_id IN (SELECT id FROM y)), 0) AS collected
                UNION ALL
                SELECT 'Advertisements',
                       ISNULL((SELECT SUM(collected) FROM ad_bookings
                               WHERE org_id = @orgId AND year_id IN (SELECT id FROM y)), 0)
                UNION ALL
                SELECT 'Special donations',
                       ISNULL((SELECT SUM(collected) FROM special_donations
                               WHERE org_id = @orgId AND year_id IN (SELECT id FROM y)), 0)
                UNION ALL
                SELECT 'Offerings (value)',
                       ISNULL((SELECT SUM(value) FROM offerings
                               WHERE org_id = @orgId AND year_id IN (SELECT id FROM y)), 0)
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "source",
            ValueColumns: ["collected"]),

        new AsuraTool(
            Id: "monthly_collection_trend",
            Title: "Collection trend by month",
            Question: "How did collections build up month by month?",
            Sql: $"""
                {Year}
                SELECT FORMAT({CollectedDate}, 'yyyy-MM') AS month,
                       COUNT(*) AS payments,
                       ISNULL(SUM(s.collected), 0) AS collected
                FROM subscriptions s
                WHERE s.org_id = @orgId
                  AND s.year_id IN (SELECT id FROM y)
                  AND {CollectedDate} IS NOT NULL
                GROUP BY FORMAT({CollectedDate}, 'yyyy-MM')
                ORDER BY month
                """,
            Parameters: [YearParam],
            Chart: "line",
            LabelColumn: "month",
            ValueColumns: ["collected"]),

        // ---------------- spending --------------------------------------
        new AsuraTool(
            Id: "budget_vs_actual",
            Title: "Budget versus actual",
            Question: "How does spending compare to the budget, and what is over?",
            Sql: $"""
                {Year}
                SELECT b.category AS category,
                       ISNULL(b.forecast, 0) AS budgeted,
                       ISNULL(e.actual, 0) AS actual,
                       ISNULL(b.forecast, 0) - ISNULL(e.actual, 0) AS remaining,
                       CASE WHEN ISNULL(b.forecast, 0) = 0 THEN 0
                            ELSE CAST(100.0 * ISNULL(e.actual, 0) / b.forecast AS DECIMAL(6,2))
                       END AS pct_used
                FROM budget_lines b
                LEFT JOIN (
                    SELECT category, SUM(amount) AS actual
                    FROM expenses
                    WHERE org_id = @orgId AND year_id IN (SELECT id FROM y)
                    GROUP BY category
                ) e ON e.category = b.category
                WHERE b.org_id = @orgId AND b.year_id IN (SELECT id FROM y)
                ORDER BY pct_used DESC
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "category",
            ValueColumns: ["budgeted", "actual"],
            Drill: new AsuraDrill("category_expense_detail", "category", "category")),

        new AsuraTool(
            Id: "expenses_by_category",
            Title: "Spending by category",
            Question: "What has the committee spent, broken down by category?",
            Sql: $"""
                {Year}
                SELECT e.category AS category,
                       COUNT(*) AS bills,
                       ISNULL(SUM(e.amount), 0) AS amount
                FROM expenses e
                WHERE e.org_id = @orgId AND e.year_id IN (SELECT id FROM y)
                GROUP BY e.category
                ORDER BY amount DESC
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "category",
            ValueColumns: ["amount"],
            Drill: new AsuraDrill("category_expense_detail", "category", "category")),

        new AsuraTool(
            Id: "category_expense_detail",
            Title: "Bills in a category",
            Question: "Show the individual bills behind one spending category.",
            Sql: $"""
                {Year}
                SELECT e.spent_on,
                       e.details,
                       v.name AS vendor,
                       e.bill_no,
                       e.mode,
                       ISNULL(e.amount, 0) AS amount
                FROM expenses e
                LEFT JOIN vendors v ON v.id = e.vendor_id AND v.org_id = @orgId
                WHERE e.org_id = @orgId
                  AND e.year_id IN (SELECT id FROM y)
                  AND e.category = @category
                ORDER BY TRY_CONVERT(date, e.spent_on) DESC
                """,
            Parameters: [YearParam, new("category", "string", true, "The spending category to expand.")],
            Chart: "none",
            LabelColumn: "details",
            ValueColumns: ["amount"]),

        new AsuraTool(
            Id: "vendor_spend",
            Title: "Vendor spend",
            Question: "Which vendors were paid the most this year?",
            Sql: $"""
                {Year}
                SELECT v.name AS vendor,
                       v.category,
                       COUNT(e.id) AS bills,
                       ISNULL(SUM(e.amount), 0) AS paid
                FROM vendors v
                LEFT JOIN expenses e ON e.vendor_id = v.id AND e.org_id = @orgId
                     AND e.year_id IN (SELECT id FROM y)
                WHERE v.org_id = @orgId
                GROUP BY v.name, v.category
                HAVING ISNULL(SUM(e.amount), 0) > 0
                ORDER BY paid DESC
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "vendor",
            ValueColumns: ["paid"]),

        new AsuraTool(
            Id: "advances_outstanding",
            Title: "Cash advances not yet settled",
            Question: "Which cash advances are still outstanding, and with whom?",
            Sql: $"""
                {Year}
                SELECT a.issued_to_name AS issued_to,
                       c.name AS collector,
                       a.purpose,
                       a.issued_on,
                       ISNULL(a.amount, 0) AS issued,
                       ISNULL(s.settled, 0) AS settled,
                       ISNULL(a.amount, 0) - ISNULL(s.settled, 0) AS outstanding,
                       a.status
                FROM advances a
                LEFT JOIN collectors c ON c.id = a.issued_to_collector_id AND c.org_id = @orgId
                LEFT JOIN (
                    SELECT advance_id, SUM(amount) AS settled
                    FROM advance_settlements
                    WHERE org_id = @orgId
                    GROUP BY advance_id
                ) s ON s.advance_id = a.id
                WHERE a.org_id = @orgId
                  AND a.year_id IN (SELECT id FROM y)
                  AND ISNULL(a.amount, 0) - ISNULL(s.settled, 0) > 0
                ORDER BY outstanding DESC
                """,
            Parameters: [YearParam],
            Chart: "bar",
            LabelColumn: "issued_to",
            ValueColumns: ["outstanding"]),

        // ---------------- headline ---------------------------------------
        new AsuraTool(
            Id: "year_summary",
            Title: "Year at a glance",
            Question: "Give me the overall position: total income, total spending, and the balance.",
            Sql: $"""
                {Year}
                SELECT py.year,
                       py.name AS year_name,
                       ISNULL(py.target, 0) AS target,
                       ISNULL((SELECT SUM(collected) FROM subscriptions
                               WHERE org_id = @orgId AND year_id = py.id), 0)
                     + ISNULL((SELECT SUM(collected) FROM ad_bookings
                               WHERE org_id = @orgId AND year_id = py.id), 0)
                     + ISNULL((SELECT SUM(collected) FROM special_donations
                               WHERE org_id = @orgId AND year_id = py.id), 0) AS total_income,
                       ISNULL((SELECT SUM(amount) FROM expenses
                               WHERE org_id = @orgId AND year_id = py.id), 0) AS total_spent,
                       ISNULL((SELECT SUM(forecast) FROM budget_lines
                               WHERE org_id = @orgId AND year_id = py.id), 0) AS total_budgeted
                FROM puja_years py
                WHERE py.org_id = @orgId AND py.id IN (SELECT id FROM y)
                """,
            Parameters: [YearParam],
            Chart: "none",
            LabelColumn: "year_name",
            ValueColumns: ["total_income", "total_spent"]),
    }.ToFrozenDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string? id, out AsuraTool tool)
    {
        tool = null!;
        return id is not null && Tools.TryGetValue(id, out tool!);
    }

    /// <summary>
    /// The menu handed to the model when it is choosing a report, and shown to
    /// the user when no report fits. Ids and questions only — the SQL is never
    /// put in front of the model, so it cannot be talked into editing it.
    /// </summary>
    public static string Menu() => string.Join("\n",
        Tools.Values.Select(t =>
            $"- {t.Id}: {t.Question}" +
            (t.Parameters.Count > 0
                ? $" [params: {string.Join(", ", t.Parameters.Select(p => p.Name + (p.Required ? "*" : "")))}]"
                : "")));
}
