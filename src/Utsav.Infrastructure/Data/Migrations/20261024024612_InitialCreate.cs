using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utsav.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    city = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    entra_tenant_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("organizations_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    full_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    role = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'collector'"),
                    auth_provider = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'local'"),
                    entra_object_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    google_sub = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("users_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "advertisers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    contact = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("advertisers_pkey", x => x.id);
                    table.ForeignKey(
                        name: "advertisers_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "lookups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: true, defaultValue: 0),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("lookups_pkey", x => x.id);
                    table.ForeignKey(
                        name: "lookups_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

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

            migrationBuilder.CreateTable(
                name: "org_role_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entra_group_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    entra_group_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    role = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("org_role_mappings_pkey", x => x.id);
                    table.ForeignKey(
                        name: "org_role_mappings_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "puja_years",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    target = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    projection_pct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 6.50m),
                    opening_cash_balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    opening_bank_balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("puja_years_pkey", x => x.id);
                    table.ForeignKey(
                        name: "puja_years_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    contact = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    address = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    gst_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("vendors_pkey", x => x.id);
                    table.ForeignKey(
                        name: "vendors_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: true, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("zones_pkey", x => x.id);
                    table.ForeignKey(
                        name: "zones_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

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

            migrationBuilder.CreateTable(
                name: "collectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "int", nullable: true, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("collectors_pkey", x => x.id);
                    table.ForeignKey(
                        name: "collectors_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "collectors_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "org_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'viewer'"),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'active'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("org_members_pkey", x => x.id);
                    table.ForeignKey(
                        name: "org_members_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "org_members_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("sessions_pkey", x => x.token);
                    table.ForeignKey(
                        name: "sessions_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "sessions_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ad_bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    advertiser_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ad_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    projected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    billed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    billed_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    due = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, computedColumnSql: "(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    comments = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ad_bookings_pkey", x => x.id);
                    table.ForeignKey(
                        name: "ad_bookings_advertiser_id_fkey",
                        column: x => x.advertiser_id,
                        principalTable: "advertisers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "ad_bookings_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "ad_bookings_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    forecast = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    contract_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    sort_order = table.Column<int>(type: "int", nullable: true, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("budget_lines_pkey", x => x.id);
                    table.ForeignKey(
                        name: "budget_lines_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "budget_lines_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cash_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    party = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    entry_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("cash_ledger_pkey", x => x.id);
                    table.ForeignKey(
                        name: "cash_ledger_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "cash_ledger_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    paid_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recorded_by_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("collection_payments_pkey", x => x.id);
                    table.ForeignKey(
                        name: "collection_payments_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "collection_payments_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fund_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    direction = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'cash_to_bank'"),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    transfer_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("fund_transfers_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fund_transfers_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fund_transfers_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offerings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    contact = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    offering_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    puja_day = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    value = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    details = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    expected_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    received_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    received_by = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'pledged'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("offerings_pkey", x => x.id);
                    table.ForeignKey(
                        name: "offerings_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "offerings_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "special_donations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    contact = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    projected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    billed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    collected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    due = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, computedColumnSql: "(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    comments = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("special_donations_pkey", x => x.id);
                    table.ForeignKey(
                        name: "special_donations_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "special_donations_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    contract_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    advance_paid = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    start_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    end_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'draft'"),
                    terms = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("contracts_pkey", x => x.id);
                    table.ForeignKey(
                        name: "contracts_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "contracts_vendor_id_fkey",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "contracts_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feast_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    details = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    spent_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    comment = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("feast_expenses_pkey", x => x.id);
                    table.ForeignKey(
                        name: "feast_expenses_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "feast_expenses_vendor_id_fkey",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "feast_expenses_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "donors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    surname = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    contact = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    zone_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("donors_pkey", x => x.id);
                    table.ForeignKey(
                        name: "donors_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "donors_zone_id_fkey",
                        column: x => x.zone_id,
                        principalTable: "zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "advances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    issued_to_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    issued_to_collector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    issued_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    purpose = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    issued_by = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'open'"),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("advances_pkey", x => x.id);
                    table.ForeignKey(
                        name: "advances_issued_to_collector_id_fkey",
                        column: x => x.issued_to_collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "advances_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "advances_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "puja_schedule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    puja_day = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    event_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    start_time = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    end_time = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    charge_title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    assignee_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    assignee_collector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: true, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("puja_schedule_pkey", x => x.id);
                    table.ForeignKey(
                        name: "puja_schedule_assignee_collector_id_fkey",
                        column: x => x.assignee_collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "puja_schedule_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "puja_schedule_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    owner_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    owner_collector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'not_started'"),
                    due_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    completed_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: true, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("workflow_items_pkey", x => x.id);
                    table.ForeignKey(
                        name: "workflow_items_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "workflow_items_owner_collector_id_fkey",
                        column: x => x.owner_collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "workflow_items_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contract_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    milestone = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    due_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    paid_date = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    expense_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'pending'"),
                    note = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("contract_payments_pkey", x => x.id);
                    table.ForeignKey(
                        name: "contract_payments_contract_id_fkey",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "contract_payments_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

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

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    details = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    spent_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    comment = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contract_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    advance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("expenses_pkey", x => x.id);
                    table.ForeignKey(
                        name: "expenses_contract_id_fkey",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "expenses_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "expenses_vendor_id_fkey",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "expenses_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    vendor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contract_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    issue_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    occurred_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValueSql: "'open'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("vendor_issues_pkey", x => x.id);
                    table.ForeignKey(
                        name: "vendor_issues_contract_id_fkey",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "vendor_issues_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "vendor_issues_vendor_id_fkey",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    donor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    not_eligible = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    billed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    billed_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    due = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, computedColumnSql: "(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    comments = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("feasts_pkey", x => x.id);
                    table.ForeignKey(
                        name: "feasts_collector_id_fkey",
                        column: x => x.collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "feasts_donor_id_fkey",
                        column: x => x.donor_id,
                        principalTable: "donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "feasts_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "feasts_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    donor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "membership"),
                    prior_billed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    projected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    bill_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    not_eligible = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    billed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    billed_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, defaultValue: 0m),
                    due = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true, computedColumnSql: "(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true),
                    mode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collected_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    collector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    comments = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    history = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("subscriptions_pkey", x => x.id);
                    table.ForeignKey(
                        name: "subscriptions_collector_id_fkey",
                        column: x => x.collector_id,
                        principalTable: "collectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "subscriptions_donor_id_fkey",
                        column: x => x.donor_id,
                        principalTable: "donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "subscriptions_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "subscriptions_year_id_fkey",
                        column: x => x.year_id,
                        principalTable: "puja_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    workflow_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    author_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("workflow_notes_pkey", x => x.id);
                    table.ForeignKey(
                        name: "workflow_notes_author_id_fkey",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "workflow_notes_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "workflow_notes_workflow_item_id_fkey",
                        column: x => x.workflow_item_id,
                        principalTable: "workflow_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "advance_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    advance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    expense_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    settled_on = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("advance_settlements_pkey", x => x.id);
                    table.ForeignKey(
                        name: "advance_settlements_advance_id_fkey",
                        column: x => x.advance_id,
                        principalTable: "advances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "advance_settlements_expense_id_fkey",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "advance_settlements_org_id_fkey",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

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
                name: "idx_ad_year",
                table: "ad_bookings",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_ad_bookings_advertiser_id",
                table: "ad_bookings",
                column: "advertiser_id");

            migrationBuilder.CreateIndex(
                name: "IX_ad_bookings_org_id_year_id",
                table: "ad_bookings",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "idx_settlements_adv",
                table: "advance_settlements",
                column: "advance_id");

            migrationBuilder.CreateIndex(
                name: "IX_advance_settlements_expense_id",
                table: "advance_settlements",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "IX_advance_settlements_org_id",
                table: "advance_settlements",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_advances_year",
                table: "advances",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_advances_issued_to_collector_id",
                table: "advances",
                column: "issued_to_collector_id");

            migrationBuilder.CreateIndex(
                name: "IX_advances_org_id_year_id_status",
                table: "advances",
                columns: new[] { "org_id", "year_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_advertisers_org_id",
                table: "advertisers",
                column: "org_id");

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

            migrationBuilder.CreateIndex(
                name: "idx_budget_year",
                table: "budget_lines",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_lines_org_id_year_id",
                table: "budget_lines",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "idx_cash_year",
                table: "cash_ledger",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_org_id_year_id",
                table: "cash_ledger",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "idx_collection_payments_parent",
                table: "collection_payments",
                columns: new[] { "parent_type", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "idx_collection_payments_year",
                table: "collection_payments",
                columns: new[] { "year_id", "org_id" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_payments_org_id",
                table: "collection_payments",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "collectors_org_name_key",
                table: "collectors",
                columns: new[] { "org_id", "name" },
                unique: true,
                filter: "[org_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_collectors_user_id",
                table: "collectors",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_cpayments_ctr",
                table: "contract_payments",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_contract_payments_org_id",
                table: "contract_payments",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_contracts_year",
                table: "contracts",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_contracts_org_id_year_id",
                table: "contracts",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contracts_vendor_id",
                table: "contracts",
                column: "vendor_id");

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

            migrationBuilder.CreateIndex(
                name: "idx_donors_org",
                table: "donors",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "IX_donors_zone_id",
                table: "donors",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "idx_expenses_org",
                table: "expenses",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_expenses_year",
                table: "expenses",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_contract_id",
                table: "expenses",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_org_id_year_id_category",
                table: "expenses",
                columns: new[] { "org_id", "year_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_vendor_id",
                table: "expenses",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_expenses_org",
                table: "feast_expenses",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_expenses_year",
                table: "feast_expenses",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_feast_expenses_org_id_year_id_category",
                table: "feast_expenses",
                columns: new[] { "org_id", "year_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_feast_expenses_vendor_id",
                table: "feast_expenses",
                column: "vendor_id");

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

            migrationBuilder.CreateIndex(
                name: "feasts_donor_id_year_id_key",
                table: "feasts",
                columns: new[] { "donor_id", "year_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_feast_org",
                table: "feasts",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_feast_year",
                table: "feasts",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_feasts_collector_id",
                table: "feasts",
                column: "collector_id");

            migrationBuilder.CreateIndex(
                name: "IX_feasts_org_id_year_id",
                table: "feasts",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "idx_transfers_year",
                table: "fund_transfers",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_fund_transfers_org_id_year_id",
                table: "fund_transfers",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "lookups_org_category_label_key",
                table: "lookups",
                columns: new[] { "org_id", "category", "label" },
                unique: true,
                filter: "[org_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_notifications_org_created",
                table: "notifications",
                columns: new[] { "org_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_offerings_year",
                table: "offerings",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_offerings_org_id_year_id",
                table: "offerings",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "idx_orgmembers_user",
                table: "org_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "org_members_org_id_user_id_key",
                table: "org_members",
                columns: new[] { "org_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "org_role_mappings_org_id_entra_group_id_key",
                table: "org_role_mappings",
                columns: new[] { "org_id", "entra_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "organizations_slug_key",
                table: "organizations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_puja_calendar_days_year",
                table: "puja_calendar_days",
                columns: new[] { "year_id", "org_id" });

            migrationBuilder.CreateIndex(
                name: "IX_puja_calendar_days_org_id",
                table: "puja_calendar_days",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_schedule_year",
                table: "puja_schedule",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_puja_schedule_assignee_collector_id",
                table: "puja_schedule",
                column: "assignee_collector_id");

            migrationBuilder.CreateIndex(
                name: "IX_puja_schedule_org_id_year_id",
                table: "puja_schedule",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "puja_years_org_year_key",
                table: "puja_years",
                columns: new[] { "org_id", "year" },
                unique: true,
                filter: "[org_id] IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "idx_sessions_user",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_org_id",
                table: "sessions",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_special_year",
                table: "special_donations",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_special_donations_org_id_year_id",
                table: "special_donations",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "idx_sub_org",
                table: "subscriptions",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_sub_year",
                table: "subscriptions",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_collector_id",
                table: "subscriptions",
                column: "collector_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_org_id_year_id",
                table: "subscriptions",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "subscriptions_donor_id_year_id_key",
                table: "subscriptions",
                columns: new[] { "donor_id", "year_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "users_email_key",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "users_entra_object_id_key",
                table: "users",
                column: "entra_object_id",
                unique: true,
                filter: "[entra_object_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "users_google_sub_key",
                table: "users",
                column: "google_sub",
                unique: true,
                filter: "[google_sub] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_issues_contract",
                table: "vendor_issues",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_issues_vendor",
                table: "vendor_issues",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_issues_org_id",
                table: "vendor_issues",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendors_org",
                table: "vendors",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "idx_workflow_year",
                table: "workflow_items",
                column: "year_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_items_org_id_year_id",
                table: "workflow_items",
                columns: new[] { "org_id", "year_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_items_owner_collector_id",
                table: "workflow_items",
                column: "owner_collector_id");

            migrationBuilder.CreateIndex(
                name: "idx_worknotes_item",
                table: "workflow_notes",
                column: "workflow_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notes_author_id",
                table: "workflow_notes",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notes_org_id",
                table: "workflow_notes",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "zones_org_name_key",
                table: "zones",
                columns: new[] { "org_id", "name" },
                unique: true,
                filter: "[org_id] IS NOT NULL");

            // EF does not model views, so they are applied here: a brand-new
            // database is one `dotnet ef database update` from complete.
            foreach (var view in DatabaseViews.CreateAll)
            {
                migrationBuilder.Sql(view);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var statement in DatabaseViews.DropAll)
            {
                migrationBuilder.Sql(statement);
            }

            migrationBuilder.DropTable(
                name: "ad_bookings");

            migrationBuilder.DropTable(
                name: "advance_settlements");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "budget_lines");

            migrationBuilder.DropTable(
                name: "cash_ledger");

            migrationBuilder.DropTable(
                name: "collection_payments");

            migrationBuilder.DropTable(
                name: "contract_payments");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "feast_expenses");

            migrationBuilder.DropTable(
                name: "feast_tokens");

            migrationBuilder.DropTable(
                name: "fund_transfers");

            migrationBuilder.DropTable(
                name: "lookups");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "offerings");

            migrationBuilder.DropTable(
                name: "org_members");

            migrationBuilder.DropTable(
                name: "org_role_mappings");

            migrationBuilder.DropTable(
                name: "puja_calendar_days");

            migrationBuilder.DropTable(
                name: "puja_schedule");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "special_donations");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "vendor_issues");

            migrationBuilder.DropTable(
                name: "workflow_notes");

            migrationBuilder.DropTable(
                name: "advertisers");

            migrationBuilder.DropTable(
                name: "advances");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "feasts");

            migrationBuilder.DropTable(
                name: "workflow_items");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "donors");

            migrationBuilder.DropTable(
                name: "collectors");

            migrationBuilder.DropTable(
                name: "vendors");

            migrationBuilder.DropTable(
                name: "puja_years");

            migrationBuilder.DropTable(
                name: "zones");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "organizations");
        }
    }
}
