using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vendor_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contract_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    issue_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    occurred_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'open'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("vendor_issues_pkey", x => x.id);
                    table.ForeignKey(
                        name: "vendor_issues_contract_id_fkey",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "vendor_issues_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "vendor_issues_vendor_id_fkey",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_vendor_issues_contract",
                table: "vendor_issues",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_issues_vendor",
                table: "vendor_issues",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_issues_org_id",
                table: "vendor_issues",
                column: "org_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_issues");
        }
    }
}
