using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    donor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    not_eligible = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    billed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    billed_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    due = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, computedColumnSql: "(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    comments = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("feasts_pkey", x => x.id);
                    table.ForeignKey(
                        name: "feasts_collector_id_fkey",
                        column: x => x.collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "feasts_donor_id_fkey",
                        column: x => x.donor_id,
                        principalTable: "donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "feasts_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "feasts_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "feasts_donor_id_year_id_key",
                table: "feasts",
                columns: new[] { "donor_id", "year_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_feast_org",
                table: "feasts",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_year",
                table: "feasts",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_feasts_collector_id",
                table: "feasts",
                column: "collector_id");

            migrationBuilder.CreateIndex(
                name: "IX_feasts_org_id_year_id",
                table: "feasts",
                columns: new[] { "org_id", "year_id" });

            // Seeds the "Pritivoj" expense category for every org that's
            // already bootstrapped (CommitteeBootstrapper only ever seeds
            // lookups into a brand-new, empty database — see its own
            // remarks — so an established committee needs this added here
            // instead). Guarded per-org so re-running this migration, or a
            // future org that bootstraps after this one, never double-inserts.
            migrationBuilder.Sql("""
                INSERT INTO lookups (id, org_id, category, label, sort_order, active)
                SELECT NEWID(), o.id, 'expense_category', 'Pritivoj',
                       COALESCE((SELECT MAX(l.sort_order) FROM lookups l
                                  WHERE l.org_id = o.id AND l.category = 'expense_category'), 0) + 1,
                       1
                  FROM organizations o
                 WHERE NOT EXISTS (
                     SELECT 1 FROM lookups l2
                      WHERE l2.org_id = o.id AND l2.category = 'expense_category' AND l2.label = 'Pritivoj');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feasts");
        }
    }
}
