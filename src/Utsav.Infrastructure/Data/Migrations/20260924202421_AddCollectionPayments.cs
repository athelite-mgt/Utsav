using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    paid_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recorded_by_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("collection_payments_pkey", x => x.id);
                    table.ForeignKey(
                        name: "collection_payments_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "collection_payments_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_collection_payments_parent",
                table: "collection_payments",
                columns: new[] { "parent_type", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "idx_collection_payments_year",
                table: "collection_payments",
                columns: new[] { "year_id", "org_id" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_payments_org_id",
                table: "collection_payments",
                column: "org_id");

            // Backfill: turn every existing collection into a single dated
            // payment row, so the fund position (which now reads these rows for
            // per-instalment cash/bank bucketing) still matches the historical
            // `collected` totals the parents already carry. Only rows with money
            // actually collected are carried; the parent's own `collected`
            // column stays as the authoritative sum, untouched.
            migrationBuilder.Sql("""
                INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode, recorded_by_name, created_at)
                SELECT NEWID(), org_id, year_id, 'subscription', id, collected, collected_on, mode, 'Carried over', SYSUTCDATETIME()
                FROM subscriptions WHERE collected IS NOT NULL AND collected > 0;
                """);
            migrationBuilder.Sql("""
                INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode, recorded_by_name, created_at)
                SELECT NEWID(), org_id, year_id, 'ad_booking', id, collected, collected_on, mode, 'Carried over', SYSUTCDATETIME()
                FROM ad_bookings WHERE collected IS NOT NULL AND collected > 0;
                """);
            migrationBuilder.Sql("""
                INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode, recorded_by_name, created_at)
                SELECT NEWID(), org_id, year_id, 'special_donation', id, collected, collected_on, mode, 'Carried over', SYSUTCDATETIME()
                FROM special_donations WHERE collected IS NOT NULL AND collected > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_payments");
        }
    }
}
