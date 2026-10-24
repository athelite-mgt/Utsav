using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    key_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    prefix = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    last_used_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("api_keys_pkey", x => x.id);
                    table.ForeignKey(
                        name: "api_keys_created_by_user_id_fkey",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "api_keys_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "api_keys_key_hash_key",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_created_by_user_id",
                table: "api_keys",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_org_id",
                table: "api_keys",
                column: "org_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");
        }
    }
}
