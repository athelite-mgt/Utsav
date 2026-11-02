using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "feast_enabled",
                table: "organizations",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at",
                table: "expenses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decided_by_name",
                table: "expenses",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "expenses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paid_from",
                table: "expenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "fund");

            migrationBuilder.AddColumn<string>(
                name: "recorded_by_name",
                table: "expenses",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recorded_by_user_id",
                table: "expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_mode",
                table: "expenses",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refunded_on",
                table: "expenses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "expenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at",
                table: "collection_payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decided_by_name",
                table: "collection_payments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "collection_payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "collection_payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "collection_payments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "approved");

            foreach (var view in DatabaseViews.AlterToApprovalAware)
            {
                migrationBuilder.Sql(view);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var view in DatabaseViews.RestoreOriginals())
            {
                migrationBuilder.Sql(view);
            }

            migrationBuilder.DropColumn(
                name: "feast_enabled",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "decided_by_name",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "paid_from",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "recorded_by_name",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "recorded_by_user_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "refund_mode",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "refunded_on",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "status",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "collection_payments");

            migrationBuilder.DropColumn(
                name: "decided_by_name",
                table: "collection_payments");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "collection_payments");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "collection_payments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "collection_payments");
        }
    }
}
