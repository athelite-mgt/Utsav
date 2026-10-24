using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    body = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    url = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("notifications_pkey", x => x.id);
                    table.ForeignKey(
                        name: "notifications_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_notifications_org_created",
                table: "notifications",
                columns: new[] { "org_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}
