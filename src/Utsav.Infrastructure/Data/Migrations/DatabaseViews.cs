namespace Utsav.Infrastructure.Data.Migrations;

/// <summary>
/// T-SQL translations of the three reporting views from
/// <c>Node/server/schema-postgres.sql</c>. EF migrations don't model views, so
/// they're applied as raw SQL from the migration that creates the tables —
/// which keeps "stand up a new committee" a single
/// <c>dotnet ef database update</c> against an empty database.
///
/// Two Postgres constructs have no SQL Server equivalent and are rewritten:
///
///   • <c>SUM(x) FILTER (WHERE cond)</c> → <c>SUM(CASE WHEN cond THEN x END)</c>.
///     Both skip non-matching rows entirely rather than counting them as zero,
///     so a fully-returned advance still reports <c>spent = NULL</c> → 0 via
///     the surrounding COALESCE, exactly as before.
///
///   • <c>GREATEST(x, 0)</c> → <c>CASE WHEN x &gt; 0 THEN x ELSE 0 END</c>.
///     Azure SQL does have GREATEST, but the CASE form also runs on SQL Server
///     2019 and earlier, so a committee self-hosting on an older instance
///     isn't shut out for no reason.
/// </summary>
internal static class DatabaseViews
{
    /// <summary>
    /// Budget line vs. what was actually spent, with unspent contract
    /// commitments folded into "actual" so a signed-but-unpaid contract still
    /// shows as money already accounted for. <c>prior_actual</c> reaches back
    /// to the previous puja year for the same category, which is what the
    /// rollover seeds next year's forecast from.
    ///
    /// <c>contracted</c> is the live total signed for this category on the
    /// Vendors &amp; Contracts page (every non-cancelled <c>contracts</c> row,
    /// summed) — distinct from <c>contract_amount</c>, which is a manually
    /// typed planning figure on the budget line itself and never reconciled
    /// against a real vendor contract.
    /// </summary>
    public const string CreateBudgetVsActual = """
        CREATE VIEW v_budget_vs_actual AS
        SELECT
          b.id, b.org_id, b.year_id, b.category, b.notes, b.forecast,
          b.contract_amount, b.sort_order,
          COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0) AS actual,
          COALESCE(b.forecast, 0) - (COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0)) AS remaining,
          COALESCE(pe.actual, 0) AS prior_actual,
          COALESCE(cc.total, 0) AS contracted
        FROM budget_lines b
        JOIN puja_years py ON py.id = b.year_id
        LEFT JOIN (
          SELECT year_id, category, SUM(amount) AS actual
          FROM expenses GROUP BY year_id, category
        ) e ON e.year_id = b.year_id AND e.category = b.category
        LEFT JOIN (
          SELECT c.year_id, c.category,
                 SUM(c.contract_amount) AS total,
                 CASE WHEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0) > 0
                      THEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0)
                      ELSE 0 END AS unspent
          FROM contracts c
          LEFT JOIN (
            SELECT contract_id, SUM(amount) AS paid FROM expenses WHERE contract_id IS NOT NULL GROUP BY contract_id
          ) cp ON cp.contract_id = c.id
          WHERE c.status <> 'cancelled'
          GROUP BY c.year_id, c.category
        ) cc ON cc.year_id = b.year_id AND cc.category = b.category
        LEFT JOIN puja_years ppy
          ON ppy.org_id = b.org_id
         AND ppy.year = (SELECT MAX(py2.year) FROM puja_years py2 WHERE py2.org_id = b.org_id AND py2.year < py.year)
        LEFT JOIN (
          SELECT year_id, category, SUM(amount) AS actual
          FROM expenses GROUP BY year_id, category
        ) pe ON pe.year_id = ppy.id AND pe.category = b.category

        UNION ALL

        -- A category can be spent against (Expenses lets anyone pick any
        -- expense_category lookup value) without anyone ever having created a
        -- matching budget_lines row. Before this, that money was invisible on
        -- the Budget page entirely — the view only ever started from
        -- budget_lines. Surfaced here with forecast=0 so real spend is never
        -- silently missing, sorted to the very end so set-up budget lines
        -- still lead the table.
        SELECT
          NEWID() AS id, py2b.org_id, ex.year_id, ex.category,
          N'No budget line set for this category — spend recorded, forecast not.' AS notes,
          0 AS forecast, 0 AS contract_amount, 2147483647 AS sort_order,
          ex.actual AS actual,
          -ex.actual AS remaining,
          COALESCE(pe2.actual, 0) AS prior_actual,
          0 AS contracted
        FROM (
          SELECT year_id, category, SUM(amount) AS actual
          FROM expenses WHERE category IS NOT NULL GROUP BY year_id, category
        ) ex
        JOIN puja_years py2b ON py2b.id = ex.year_id
        LEFT JOIN puja_years ppy2
          ON ppy2.org_id = py2b.org_id
         AND ppy2.year = (SELECT MAX(py3.year) FROM puja_years py3 WHERE py3.org_id = py2b.org_id AND py3.year < py2b.year)
        LEFT JOIN (
          SELECT year_id, category, SUM(amount) AS actual
          FROM expenses GROUP BY year_id, category
        ) pe2 ON pe2.year_id = ppy2.id AND pe2.category = ex.category
        WHERE NOT EXISTS (
          SELECT 1 FROM budget_lines b2 WHERE b2.year_id = ex.year_id AND b2.category = ex.category
        );
        """;

    /// <summary>
    /// Contract vs. money actually paid against it. <c>paid</c> deliberately
    /// comes from real expense rows, never from the payment schedule — a
    /// milestone being marked paid doesn't move money.
    /// </summary>
    public const string CreateContractStatus = """
        CREATE VIEW v_contract_status AS
        SELECT
          c.id, c.org_id, c.year_id, c.vendor_id, c.title, c.category,
          c.contract_amount, c.start_date, c.end_date, c.status, c.terms,
          c.notes, c.created_at,
          v.name AS vendor_name,
          COALESCE(e.paid, 0) AS paid,
          COALESCE(p.scheduled, 0) AS scheduled,
          c.contract_amount - COALESCE(e.paid, 0) AS outstanding
        FROM contracts c
        JOIN vendors v ON v.id = c.vendor_id
        LEFT JOIN (
          SELECT contract_id, SUM(amount) AS paid
          FROM expenses WHERE contract_id IS NOT NULL GROUP BY contract_id
        ) e ON e.contract_id = c.id
        LEFT JOIN (
          SELECT contract_id, SUM(amount) AS scheduled
          FROM contract_payments WHERE status <> 'cancelled' GROUP BY contract_id
        ) p ON p.contract_id = c.id;
        """;

    /// <summary>
    /// How much of each cash advance is still out. <c>outstanding</c> is what
    /// the fund position subtracts: money that has physically left the box but
    /// has no expense against it yet.
    /// </summary>
    public const string CreateAdvanceBalances = """
        CREATE VIEW v_advance_balances AS
        SELECT
          a.id, a.org_id, a.year_id, a.issued_to_name, a.issued_to_collector_id,
          a.amount, a.issued_on, a.purpose, a.issued_by, a.mode, a.status,
          a.notes, a.created_at,
          COALESCE(s.settled, 0) AS settled,
          COALESCE(s.spent, 0) AS spent,
          COALESCE(s.returned, 0) AS returned,
          a.amount - COALESCE(s.settled, 0) AS outstanding
        FROM advances a
        LEFT JOIN (
          SELECT advance_id,
                 SUM(amount) AS settled,
                 SUM(CASE WHEN kind = 'expense' THEN amount END) AS spent,
                 SUM(CASE WHEN kind = 'return'  THEN amount END) AS returned
          FROM advance_settlements GROUP BY advance_id
        ) s ON s.advance_id = a.id;
        """;

    public static readonly string[] CreateAll =
        [CreateBudgetVsActual, CreateContractStatus, CreateAdvanceBalances];

    public static readonly string[] DropAll =
    [
        "DROP VIEW IF EXISTS v_budget_vs_actual;",
        "DROP VIEW IF EXISTS v_contract_status;",
        "DROP VIEW IF EXISTS v_advance_balances;",
    ];
}
