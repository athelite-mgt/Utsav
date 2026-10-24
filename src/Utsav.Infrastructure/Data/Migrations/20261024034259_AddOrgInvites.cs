using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "org_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    redeemed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    redeemed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_invites", x => x.id);
                    table.ForeignKey(
                        name: "FK_org_invites_organizations_org_id",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_org_invites_org_id",
                table: "org_invites",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "org_invites_code_hash_key",
                table: "org_invites",
                column: "code_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "org_invites");
        }
    }
}
