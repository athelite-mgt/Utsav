using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    endpoint = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    p256dh = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    auth = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    user_agent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("push_subscriptions_pkey", x => x.id);
                    table.ForeignKey(
                        name: "push_subscriptions_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "push_subscriptions_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_push_subscriptions_user",
                table: "push_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_org_id",
                table: "push_subscriptions",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "push_subscriptions_endpoint_key",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");
        }
    }
}
