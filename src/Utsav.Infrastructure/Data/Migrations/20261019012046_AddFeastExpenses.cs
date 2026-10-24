using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeastExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feast_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    details = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    spent_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    comment = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("feast_expenses_pkey", x => x.id);
                    table.ForeignKey(
                        name: "feast_expenses_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "feast_expenses_vendor_id_fkey",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "feast_expenses_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_feast_expenses_org",
                table: "feast_expenses",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_expenses_year",
                table: "feast_expenses",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_feast_expenses_org_id_year_id_category",
                table: "feast_expenses",
                columns: new[] { "org_id", "year_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_feast_expenses_vendor_id",
                table: "feast_expenses",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feast_expenses");
        }
    }
}
