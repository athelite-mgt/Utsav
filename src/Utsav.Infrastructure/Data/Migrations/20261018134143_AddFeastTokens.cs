using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeastTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feast_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    feast_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "unassigned"),
                    assigned_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    redeemed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    redeemed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    redeemed_by_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("feast_tokens_pkey", x => x.id);
                    table.ForeignKey(
                        name: "feast_tokens_feast_id_fkey",
                        column: x => x.feast_id,
                        principalTable: "feasts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "feast_tokens_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "feast_tokens_redeemed_by_user_id_fkey",
                        column: x => x.redeemed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "feast_tokens_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "feast_tokens_code_key",
                table: "feast_tokens",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_feast_tokens_feast",
                table: "feast_tokens",
                column: "feast_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_tokens_org",
                table: "feast_tokens",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_tokens_status",
                table: "feast_tokens",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_feast_tokens_year",
                table: "feast_tokens",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_feast_tokens_org_id_year_id",
                table: "feast_tokens",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "IX_feast_tokens_redeemed_by_user_id",
                table: "feast_tokens",
                column: "redeemed_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feast_tokens");
        }
    }
}
