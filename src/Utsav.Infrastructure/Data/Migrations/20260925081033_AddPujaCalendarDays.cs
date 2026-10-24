using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPujaCalendarDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "puja_calendar_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    event_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    note = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("puja_calendar_days_pkey", x => x.id);
                    table.ForeignKey(
                        name: "puja_calendar_days_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "puja_calendar_days_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_puja_calendar_days_year",
                table: "puja_calendar_days",
                columns: new[] { "year_id", "org_id" });

            migrationBuilder.CreateIndex(
                name: "IX_puja_calendar_days_org_id",
                table: "puja_calendar_days",
                column: "org_id");

            // Seed the 2026 Durga Puja calendar (Surja Siddhanta reckoning) for
            // the default committee's 2026 year — the dates confirmed with Boss.
            // Guarded: only runs if that year exists and has no calendar rows yet,
            // so re-running or a fresh committee without a 2026 year is a no-op.
            migrationBuilder.Sql("""
                DECLARE @org UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
                DECLARE @year UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM puja_years WHERE org_id=@org AND year=2026 ORDER BY is_active DESC);
                IF @year IS NOT NULL AND NOT EXISTS (SELECT 1 FROM puja_calendar_days WHERE year_id=@year AND org_id=@org)
                BEGIN
                    INSERT INTO puja_calendar_days (id, org_id, year_id, label, event_date, note, sort_order, created_at) VALUES
                        (NEWID(), @org, @year, 'Mahalaya',  '2026-10-10', NULL, 1, SYSUTCDATETIME()),
                        (NEWID(), @org, @year, 'Panchami',  '2026-10-16', NULL, 2, SYSUTCDATETIME()),
                        (NEWID(), @org, @year, 'Sasthi',    '2026-10-17', NULL, 3, SYSUTCDATETIME()),
                        (NEWID(), @org, @year, 'Saptami',   '2026-10-18', NULL, 4, SYSUTCDATETIME()),
                        (NEWID(), @org, @year, 'Astami',    '2026-10-19', N'Sandhi Puja 10:27–11:15 AM (Surja Siddhanta)', 5, SYSUTCDATETIME()),
                        (NEWID(), @org, @year, 'Nabami',    '2026-10-20', NULL, 6, SYSUTCDATETIME()),
                        (NEWID(), @org, @year, 'Dasami',    '2026-10-21', NULL, 7, SYSUTCDATETIME());
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "puja_calendar_days");
        }
    }
}
