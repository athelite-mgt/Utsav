using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SurfaceOrphanBudgetCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF doesn't model views — drop and recreate from the single
            // source of truth in DatabaseViews.cs, same as InitialCreate does.
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_budget_vs_actual;");
            migrationBuilder.Sql(DatabaseViews.CreateBudgetVsActual);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_budget_vs_actual;");
            migrationBuilder.Sql("""
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
                ) pe ON pe.year_id = ppy.id AND pe.category = b.category;
                """);
        }
    }
}
