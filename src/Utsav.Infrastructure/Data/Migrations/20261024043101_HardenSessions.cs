using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sessions are now stored as a hash of the token the browser holds, so
            // every existing row is unusable. Remove them rather than leave
            // raw credentials sitting in the table; everyone simply signs in again.
            migrationBuilder.Sql("DELETE FROM sessions;");

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "sessions",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "DATEADD(day, 30, SYSUTCDATETIME())");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_seen_at",
                table: "sessions",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "sessions");
        }
    }
}
