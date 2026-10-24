using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "subscriptions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "membership");

            // subscriptions is reachable through the generic CRUD routes, so
            // the value arrives straight from a client PATCH — the database is
            // the only place that can guarantee it stays one of the two the
            // UI knows how to display.
            migrationBuilder.Sql(
                "ALTER TABLE [subscriptions] ADD CONSTRAINT [CK_subscriptions_category] " +
                "CHECK ([category] IN ('membership', 'special'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [subscriptions] DROP CONSTRAINT [CK_subscriptions_category];");

            migrationBuilder.DropColumn(
                name: "category",
                table: "subscriptions");
        }
    }
}
