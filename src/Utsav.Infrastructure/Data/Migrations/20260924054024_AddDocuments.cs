using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contract_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    blob_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("documents_pkey", x => x.id);
                    table.ForeignKey(
                        name: "documents_contract_id_fkey",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "documents_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "documents_uploaded_by_user_id_fkey",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "documents_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_documents_contract",
                table: "documents",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "idx_documents_year",
                table: "documents",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_org_id",
                table: "documents",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_uploaded_by_user_id",
                table: "documents",
                column: "uploaded_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
