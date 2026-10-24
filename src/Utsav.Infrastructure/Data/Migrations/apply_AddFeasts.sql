IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [organizations] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [name] nvarchar(256) NOT NULL,
        [slug] nvarchar(256) NOT NULL,
        [city] nvarchar(256) NULL,
        [entra_tenant_id] nvarchar(256) NULL,
        [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [organizations_pkey] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [users] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [email] nvarchar(320) NOT NULL,
        [password_hash] nvarchar(256) NULL,
        [full_name] nvarchar(256) NULL,
        [role] nvarchar(256) NOT NULL DEFAULT ('collector'),
        [auth_provider] nvarchar(256) NOT NULL DEFAULT ('local'),
        [entra_object_id] nvarchar(256) NULL,
        [google_sub] nvarchar(256) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [users_pkey] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [advertisers] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [name] nvarchar(256) NOT NULL,
        [contact] nvarchar(256) NULL,
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [advertisers_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [advertisers_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [lookups] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [category] nvarchar(256) NOT NULL,
        [label] nvarchar(256) NOT NULL,
        [sort_order] int NULL DEFAULT 0,
        [active] bit NOT NULL DEFAULT CAST(1 AS bit),
        CONSTRAINT [lookups_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [lookups_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [org_role_mappings] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NOT NULL,
        [entra_group_id] nvarchar(256) NOT NULL,
        [entra_group_name] nvarchar(256) NULL,
        [role] nvarchar(256) NOT NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [org_role_mappings_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [org_role_mappings_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [puja_years] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year] int NOT NULL,
        [name] nvarchar(256) NULL,
        [target] decimal(12,2) NULL DEFAULT 0.0,
        [projection_pct] decimal(5,2) NOT NULL DEFAULT 6.5,
        [is_active] bit NOT NULL,
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [puja_years_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [puja_years_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [vendors] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [name] nvarchar(256) NOT NULL,
        [contact] nvarchar(256) NULL,
        [email] nvarchar(256) NULL,
        [address] nvarchar(max) NULL,
        [gst_no] nvarchar(256) NULL,
        [category] nvarchar(256) NULL,
        [active] bit NOT NULL DEFAULT CAST(1 AS bit),
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [vendors_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [vendors_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [zones] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [name] nvarchar(256) NOT NULL,
        [sort_order] int NULL DEFAULT 0,
        CONSTRAINT [zones_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [zones_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [collectors] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [name] nvarchar(256) NOT NULL,
        [phone] nvarchar(256) NULL,
        [user_id] uniqueidentifier NULL,
        [active] bit NOT NULL DEFAULT CAST(1 AS bit),
        [sort_order] int NULL DEFAULT 0,
        CONSTRAINT [collectors_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [collectors_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [collectors_user_id_fkey] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [org_members] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [role] nvarchar(256) NOT NULL DEFAULT ('viewer'),
        [status] nvarchar(256) NOT NULL DEFAULT ('active'),
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [org_members_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [org_members_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [org_members_user_id_fkey] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [sessions] (
        [token] nvarchar(64) NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [org_id] uniqueidentifier NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [sessions_pkey] PRIMARY KEY ([token]),
        CONSTRAINT [sessions_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [sessions_user_id_fkey] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [ad_bookings] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [advertiser_id] uniqueidentifier NOT NULL,
        [year_id] uniqueidentifier NOT NULL,
        [ad_type] nvarchar(256) NULL,
        [projected] decimal(12,2) NULL DEFAULT 0.0,
        [bill_no] nvarchar(256) NULL,
        [billed] decimal(12,2) NULL DEFAULT 0.0,
        [billed_on] nvarchar(256) NULL,
        [collected] decimal(12,2) NULL DEFAULT 0.0,
        [due] AS (COALESCE([billed], 0) - COALESCE([collected], 0)) PERSISTED,
        [mode] nvarchar(256) NULL,
        [status] nvarchar(256) NULL,
        [collected_on] nvarchar(256) NULL,
        [comments] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [ad_bookings_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [ad_bookings_advertiser_id_fkey] FOREIGN KEY ([advertiser_id]) REFERENCES [advertisers] ([id]) ON DELETE CASCADE,
        CONSTRAINT [ad_bookings_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [ad_bookings_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [budget_lines] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [category] nvarchar(256) NOT NULL,
        [notes] nvarchar(max) NULL,
        [forecast] decimal(12,2) NULL DEFAULT 0.0,
        [contract_amount] decimal(12,2) NULL DEFAULT 0.0,
        [sort_order] int NULL DEFAULT 0,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [budget_lines_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [budget_lines_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [budget_lines_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [cash_ledger] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [party] nvarchar(256) NOT NULL,
        [amount] decimal(12,2) NOT NULL,
        [entry_date] nvarchar(256) NULL,
        [note] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [cash_ledger_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [cash_ledger_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [cash_ledger_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [fund_transfers] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [direction] nvarchar(256) NOT NULL DEFAULT ('cash_to_bank'),
        [amount] decimal(12,2) NOT NULL,
        [transfer_date] nvarchar(256) NULL,
        [reference] nvarchar(256) NULL,
        [note] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [fund_transfers_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [fund_transfers_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [fund_transfers_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [offerings] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [name] nvarchar(256) NOT NULL,
        [contact] nvarchar(256) NULL,
        [offering_type] nvarchar(256) NULL,
        [puja_day] nvarchar(256) NULL,
        [details] nvarchar(max) NULL,
        [expected_date] nvarchar(256) NULL,
        [received_date] nvarchar(256) NULL,
        [received_by] nvarchar(256) NULL,
        [status] nvarchar(256) NOT NULL DEFAULT ('pledged'),
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [offerings_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [offerings_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [offerings_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [special_donations] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [name] nvarchar(256) NOT NULL,
        [contact] nvarchar(256) NULL,
        [bill_no] nvarchar(256) NULL,
        [projected] decimal(12,2) NULL DEFAULT 0.0,
        [billed] decimal(12,2) NULL DEFAULT 0.0,
        [collected] decimal(12,2) NULL DEFAULT 0.0,
        [due] AS (COALESCE([billed], 0) - COALESCE([collected], 0)) PERSISTED,
        [mode] nvarchar(256) NULL,
        [collected_on] nvarchar(256) NULL,
        [comments] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [special_donations_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [special_donations_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [special_donations_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [contracts] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [vendor_id] uniqueidentifier NOT NULL,
        [title] nvarchar(256) NOT NULL,
        [category] nvarchar(256) NULL,
        [contract_amount] decimal(12,2) NOT NULL,
        [advance_paid] decimal(12,2) NOT NULL,
        [start_date] nvarchar(256) NULL,
        [end_date] nvarchar(256) NULL,
        [status] nvarchar(256) NOT NULL DEFAULT ('draft'),
        [terms] nvarchar(max) NULL,
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [contracts_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [contracts_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [contracts_vendor_id_fkey] FOREIGN KEY ([vendor_id]) REFERENCES [vendors] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [contracts_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [donors] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [name] nvarchar(256) NOT NULL,
        [surname] nvarchar(256) NULL,
        [contact] nvarchar(256) NULL,
        [zone_id] uniqueidentifier NULL,
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [donors_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [donors_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [donors_zone_id_fkey] FOREIGN KEY ([zone_id]) REFERENCES [zones] ([id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [advances] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [issued_to_name] nvarchar(256) NOT NULL,
        [issued_to_collector_id] uniqueidentifier NULL,
        [amount] decimal(12,2) NOT NULL,
        [issued_on] nvarchar(256) NULL,
        [purpose] nvarchar(max) NULL,
        [issued_by] nvarchar(256) NULL,
        [mode] nvarchar(256) NULL,
        [status] nvarchar(256) NOT NULL DEFAULT ('open'),
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [advances_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [advances_issued_to_collector_id_fkey] FOREIGN KEY ([issued_to_collector_id]) REFERENCES [collectors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [advances_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [advances_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [puja_schedule] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [puja_day] nvarchar(256) NOT NULL,
        [event_date] nvarchar(256) NULL,
        [start_time] nvarchar(256) NULL,
        [end_time] nvarchar(256) NULL,
        [charge_title] nvarchar(256) NOT NULL,
        [assignee_name] nvarchar(256) NULL,
        [assignee_collector_id] uniqueidentifier NULL,
        [contact] nvarchar(256) NULL,
        [notes] nvarchar(max) NULL,
        [sort_order] int NULL DEFAULT 0,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [puja_schedule_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [puja_schedule_assignee_collector_id_fkey] FOREIGN KEY ([assignee_collector_id]) REFERENCES [collectors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [puja_schedule_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [puja_schedule_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [workflow_items] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [title] nvarchar(256) NOT NULL,
        [category] nvarchar(256) NULL,
        [owner_name] nvarchar(256) NULL,
        [owner_collector_id] uniqueidentifier NULL,
        [status] nvarchar(256) NOT NULL DEFAULT ('not_started'),
        [due_date] nvarchar(256) NULL,
        [completed_date] nvarchar(256) NULL,
        [notes] nvarchar(max) NULL,
        [sort_order] int NULL DEFAULT 0,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [workflow_items_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [workflow_items_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [workflow_items_owner_collector_id_fkey] FOREIGN KEY ([owner_collector_id]) REFERENCES [collectors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [workflow_items_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [contract_payments] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [contract_id] uniqueidentifier NOT NULL,
        [milestone] nvarchar(256) NULL,
        [amount] decimal(12,2) NOT NULL,
        [due_date] nvarchar(256) NULL,
        [paid_date] nvarchar(256) NULL,
        [expense_id] uniqueidentifier NULL,
        [status] nvarchar(256) NOT NULL DEFAULT ('pending'),
        [note] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [contract_payments_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [contract_payments_contract_id_fkey] FOREIGN KEY ([contract_id]) REFERENCES [contracts] ([id]) ON DELETE CASCADE,
        CONSTRAINT [contract_payments_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [expenses] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [category] nvarchar(256) NULL,
        [details] nvarchar(max) NULL,
        [amount] decimal(12,2) NOT NULL,
        [spent_on] nvarchar(256) NULL,
        [mode] nvarchar(256) NULL,
        [comment] nvarchar(max) NULL,
        [vendor_id] uniqueidentifier NULL,
        [contract_id] uniqueidentifier NULL,
        [advance_id] uniqueidentifier NULL,
        [bill_no] nvarchar(256) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [expenses_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [expenses_contract_id_fkey] FOREIGN KEY ([contract_id]) REFERENCES [contracts] ([id]),
        CONSTRAINT [expenses_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [expenses_vendor_id_fkey] FOREIGN KEY ([vendor_id]) REFERENCES [vendors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [expenses_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [subscriptions] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [donor_id] uniqueidentifier NOT NULL,
        [year_id] uniqueidentifier NOT NULL,
        [prior_billed] decimal(12,2) NULL DEFAULT 0.0,
        [projected] decimal(12,2) NULL DEFAULT 0.0,
        [bill_no] nvarchar(256) NULL,
        [billed] decimal(12,2) NULL DEFAULT 0.0,
        [billed_on] nvarchar(256) NULL,
        [collected] decimal(12,2) NULL DEFAULT 0.0,
        [due] AS (COALESCE([billed], 0) - COALESCE([collected], 0)) PERSISTED,
        [mode] nvarchar(256) NULL,
        [collected_on] nvarchar(256) NULL,
        [collector_id] uniqueidentifier NULL,
        [comments] nvarchar(max) NULL,
        [history] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [subscriptions_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [subscriptions_collector_id_fkey] FOREIGN KEY ([collector_id]) REFERENCES [collectors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [subscriptions_donor_id_fkey] FOREIGN KEY ([donor_id]) REFERENCES [donors] ([id]) ON DELETE CASCADE,
        CONSTRAINT [subscriptions_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [subscriptions_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [workflow_notes] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [workflow_item_id] uniqueidentifier NOT NULL,
        [author_id] uniqueidentifier NULL,
        [author_name] nvarchar(256) NULL,
        [note] nvarchar(max) NOT NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [workflow_notes_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [workflow_notes_author_id_fkey] FOREIGN KEY ([author_id]) REFERENCES [users] ([id]) ON DELETE SET NULL,
        CONSTRAINT [workflow_notes_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [workflow_notes_workflow_item_id_fkey] FOREIGN KEY ([workflow_item_id]) REFERENCES [workflow_items] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE TABLE [advance_settlements] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [advance_id] uniqueidentifier NOT NULL,
        [kind] nvarchar(256) NOT NULL,
        [expense_id] uniqueidentifier NULL,
        [amount] decimal(12,2) NOT NULL,
        [settled_on] nvarchar(256) NULL,
        [note] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [advance_settlements_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [advance_settlements_advance_id_fkey] FOREIGN KEY ([advance_id]) REFERENCES [advances] ([id]) ON DELETE CASCADE,
        CONSTRAINT [advance_settlements_expense_id_fkey] FOREIGN KEY ([expense_id]) REFERENCES [expenses] ([id]),
        CONSTRAINT [advance_settlements_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_ad_year] ON [ad_bookings] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ad_bookings_advertiser_id] ON [ad_bookings] ([advertiser_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ad_bookings_org_id_year_id] ON [ad_bookings] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_settlements_adv] ON [advance_settlements] ([advance_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_advance_settlements_expense_id] ON [advance_settlements] ([expense_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_advance_settlements_org_id] ON [advance_settlements] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_advances_year] ON [advances] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_advances_issued_to_collector_id] ON [advances] ([issued_to_collector_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_advances_org_id_year_id_status] ON [advances] ([org_id], [year_id], [status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_advertisers_org_id] ON [advertisers] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_budget_year] ON [budget_lines] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_budget_lines_org_id_year_id] ON [budget_lines] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_cash_year] ON [cash_ledger] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_cash_ledger_org_id_year_id] ON [cash_ledger] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [collectors_org_name_key] ON [collectors] ([org_id], [name]) WHERE [org_id] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_collectors_user_id] ON [collectors] ([user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_cpayments_ctr] ON [contract_payments] ([contract_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_contract_payments_org_id] ON [contract_payments] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_contracts_year] ON [contracts] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_contracts_org_id_year_id] ON [contracts] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_contracts_vendor_id] ON [contracts] ([vendor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_donors_org] ON [donors] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_donors_zone_id] ON [donors] ([zone_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_expenses_org] ON [expenses] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_expenses_year] ON [expenses] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_expenses_contract_id] ON [expenses] ([contract_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_expenses_org_id_year_id_category] ON [expenses] ([org_id], [year_id], [category]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_expenses_vendor_id] ON [expenses] ([vendor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_transfers_year] ON [fund_transfers] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_fund_transfers_org_id_year_id] ON [fund_transfers] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [lookups_org_category_label_key] ON [lookups] ([org_id], [category], [label]) WHERE [org_id] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_offerings_year] ON [offerings] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_offerings_org_id_year_id] ON [offerings] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_orgmembers_user] ON [org_members] ([user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [org_members_org_id_user_id_key] ON [org_members] ([org_id], [user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [org_role_mappings_org_id_entra_group_id_key] ON [org_role_mappings] ([org_id], [entra_group_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [organizations_slug_key] ON [organizations] ([slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_schedule_year] ON [puja_schedule] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_puja_schedule_assignee_collector_id] ON [puja_schedule] ([assignee_collector_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_puja_schedule_org_id_year_id] ON [puja_schedule] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [puja_years_org_year_key] ON [puja_years] ([org_id], [year]) WHERE [org_id] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_sessions_user] ON [sessions] ([user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_sessions_org_id] ON [sessions] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_special_year] ON [special_donations] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_special_donations_org_id_year_id] ON [special_donations] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_sub_org] ON [subscriptions] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_sub_year] ON [subscriptions] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_subscriptions_collector_id] ON [subscriptions] ([collector_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_subscriptions_org_id_year_id] ON [subscriptions] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [subscriptions_donor_id_year_id_key] ON [subscriptions] ([donor_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [users_email_key] ON [users] ([email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [users_entra_object_id_key] ON [users] ([entra_object_id]) WHERE [entra_object_id] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [users_google_sub_key] ON [users] ([google_sub]) WHERE [google_sub] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_vendors_org] ON [vendors] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_workflow_year] ON [workflow_items] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_workflow_items_org_id_year_id] ON [workflow_items] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_workflow_items_owner_collector_id] ON [workflow_items] ([owner_collector_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [idx_worknotes_item] ON [workflow_notes] ([workflow_item_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_workflow_notes_author_id] ON [workflow_notes] ([author_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_workflow_notes_org_id] ON [workflow_notes] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [zones_org_name_key] ON [zones] ([org_id], [name]) WHERE [org_id] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE VIEW v_budget_vs_actual AS
    SELECT
      b.id, b.org_id, b.year_id, b.category, b.notes, b.forecast,
      b.contract_amount, b.sort_order,
      COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0) AS actual,
      COALESCE(b.forecast, 0) - (COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0)) AS remaining,
      COALESCE(pe.actual, 0) AS prior_actual,
      COALESCE(cc.total, 0) AS contracted
    FROM budget_lines b
    JOIN puja_years py ON py.id = b.year_id
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) e ON e.year_id = b.year_id AND e.category = b.category
    LEFT JOIN (
      SELECT c.year_id, c.category,
             SUM(c.contract_amount) AS total,
             CASE WHEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0) > 0
                  THEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0)
                  ELSE 0 END AS unspent
      FROM contracts c
      LEFT JOIN (
        SELECT contract_id, SUM(amount) AS paid FROM expenses WHERE contract_id IS NOT NULL GROUP BY contract_id
      ) cp ON cp.contract_id = c.id
      WHERE c.status <> 'cancelled'
      GROUP BY c.year_id, c.category
    ) cc ON cc.year_id = b.year_id AND cc.category = b.category
    LEFT JOIN puja_years ppy
      ON ppy.org_id = b.org_id
     AND ppy.year = (SELECT MAX(py2.year) FROM puja_years py2 WHERE py2.org_id = b.org_id AND py2.year < py.year)
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) pe ON pe.year_id = ppy.id AND pe.category = b.category

    UNION ALL

    -- A category can be spent against (Expenses lets anyone pick any
    -- expense_category lookup value) without anyone ever having created a
    -- matching budget_lines row. Before this, that money was invisible on
    -- the Budget page entirely — the view only ever started from
    -- budget_lines. Surfaced here with forecast=0 so real spend is never
    -- silently missing, sorted to the very end so set-up budget lines
    -- still lead the table.
    SELECT
      NEWID() AS id, py2b.org_id, ex.year_id, ex.category,
      N'No budget line set for this category — spend recorded, forecast not.' AS notes,
      0 AS forecast, 0 AS contract_amount, 2147483647 AS sort_order,
      ex.actual AS actual,
      -ex.actual AS remaining,
      COALESCE(pe2.actual, 0) AS prior_actual,
      0 AS contracted
    FROM (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses WHERE category IS NOT NULL GROUP BY year_id, category
    ) ex
    JOIN puja_years py2b ON py2b.id = ex.year_id
    LEFT JOIN puja_years ppy2
      ON ppy2.org_id = py2b.org_id
     AND ppy2.year = (SELECT MAX(py3.year) FROM puja_years py3 WHERE py3.org_id = py2b.org_id AND py3.year < py2b.year)
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) pe2 ON pe2.year_id = ppy2.id AND pe2.category = ex.category
    WHERE NOT EXISTS (
      SELECT 1 FROM budget_lines b2 WHERE b2.year_id = ex.year_id AND b2.category = ex.category
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE VIEW v_contract_status AS
    SELECT
      c.id, c.org_id, c.year_id, c.vendor_id, c.title, c.category,
      c.contract_amount, c.start_date, c.end_date, c.status, c.terms,
      c.notes, c.created_at,
      v.name AS vendor_name,
      COALESCE(e.paid, 0) AS paid,
      COALESCE(p.scheduled, 0) AS scheduled,
      c.contract_amount - COALESCE(e.paid, 0) AS outstanding
    FROM contracts c
    JOIN vendors v ON v.id = c.vendor_id
    LEFT JOIN (
      SELECT contract_id, SUM(amount) AS paid
      FROM expenses WHERE contract_id IS NOT NULL GROUP BY contract_id
    ) e ON e.contract_id = c.id
    LEFT JOIN (
      SELECT contract_id, SUM(amount) AS scheduled
      FROM contract_payments WHERE status <> 'cancelled' GROUP BY contract_id
    ) p ON p.contract_id = c.id;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    CREATE VIEW v_advance_balances AS
    SELECT
      a.id, a.org_id, a.year_id, a.issued_to_name, a.issued_to_collector_id,
      a.amount, a.issued_on, a.purpose, a.issued_by, a.mode, a.status,
      a.notes, a.created_at,
      COALESCE(s.settled, 0) AS settled,
      COALESCE(s.spent, 0) AS spent,
      COALESCE(s.returned, 0) AS returned,
      a.amount - COALESCE(s.settled, 0) AS outstanding
    FROM advances a
    LEFT JOIN (
      SELECT advance_id,
             SUM(amount) AS settled,
             SUM(CASE WHEN kind = 'expense' THEN amount END) AS spent,
             SUM(CASE WHEN kind = 'return'  THEN amount END) AS returned
      FROM advance_settlements GROUP BY advance_id
    ) s ON s.advance_id = a.id;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817062311_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817062311_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827115253_AddSubscriptionCategory'
)
BEGIN
    ALTER TABLE [subscriptions] ADD [category] nvarchar(32) NOT NULL DEFAULT N'membership';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827115253_AddSubscriptionCategory'
)
BEGIN
    ALTER TABLE [subscriptions] ADD CONSTRAINT [CK_subscriptions_category] CHECK ([category] IN ('membership', 'special'));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827115253_AddSubscriptionCategory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827115253_AddSubscriptionCategory', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828091046_AddContractedToBudgetView'
)
BEGIN
    DROP VIEW IF EXISTS v_budget_vs_actual;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828091046_AddContractedToBudgetView'
)
BEGIN
    CREATE VIEW v_budget_vs_actual AS
    SELECT
      b.id, b.org_id, b.year_id, b.category, b.notes, b.forecast,
      b.contract_amount, b.sort_order,
      COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0) AS actual,
      COALESCE(b.forecast, 0) - (COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0)) AS remaining,
      COALESCE(pe.actual, 0) AS prior_actual,
      COALESCE(cc.total, 0) AS contracted
    FROM budget_lines b
    JOIN puja_years py ON py.id = b.year_id
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) e ON e.year_id = b.year_id AND e.category = b.category
    LEFT JOIN (
      SELECT c.year_id, c.category,
             SUM(c.contract_amount) AS total,
             CASE WHEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0) > 0
                  THEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0)
                  ELSE 0 END AS unspent
      FROM contracts c
      LEFT JOIN (
        SELECT contract_id, SUM(amount) AS paid FROM expenses WHERE contract_id IS NOT NULL GROUP BY contract_id
      ) cp ON cp.contract_id = c.id
      WHERE c.status <> 'cancelled'
      GROUP BY c.year_id, c.category
    ) cc ON cc.year_id = b.year_id AND cc.category = b.category
    LEFT JOIN puja_years ppy
      ON ppy.org_id = b.org_id
     AND ppy.year = (SELECT MAX(py2.year) FROM puja_years py2 WHERE py2.org_id = b.org_id AND py2.year < py.year)
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) pe ON pe.year_id = ppy.id AND pe.category = b.category

    UNION ALL

    -- A category can be spent against (Expenses lets anyone pick any
    -- expense_category lookup value) without anyone ever having created a
    -- matching budget_lines row. Before this, that money was invisible on
    -- the Budget page entirely — the view only ever started from
    -- budget_lines. Surfaced here with forecast=0 so real spend is never
    -- silently missing, sorted to the very end so set-up budget lines
    -- still lead the table.
    SELECT
      NEWID() AS id, py2b.org_id, ex.year_id, ex.category,
      N'No budget line set for this category — spend recorded, forecast not.' AS notes,
      0 AS forecast, 0 AS contract_amount, 2147483647 AS sort_order,
      ex.actual AS actual,
      -ex.actual AS remaining,
      COALESCE(pe2.actual, 0) AS prior_actual,
      0 AS contracted
    FROM (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses WHERE category IS NOT NULL GROUP BY year_id, category
    ) ex
    JOIN puja_years py2b ON py2b.id = ex.year_id
    LEFT JOIN puja_years ppy2
      ON ppy2.org_id = py2b.org_id
     AND ppy2.year = (SELECT MAX(py3.year) FROM puja_years py3 WHERE py3.org_id = py2b.org_id AND py3.year < py2b.year)
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) pe2 ON pe2.year_id = ppy2.id AND pe2.category = ex.category
    WHERE NOT EXISTS (
      SELECT 1 FROM budget_lines b2 WHERE b2.year_id = ex.year_id AND b2.category = ex.category
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828091046_AddContractedToBudgetView'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260828091046_AddContractedToBudgetView', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923135643_AddPushSubscriptions'
)
BEGIN
    CREATE TABLE [push_subscriptions] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [user_id] uniqueidentifier NOT NULL,
        [endpoint] nvarchar(1024) NOT NULL,
        [p256dh] nvarchar(256) NOT NULL,
        [auth] nvarchar(256) NOT NULL,
        [user_agent] nvarchar(512) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [push_subscriptions_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [push_subscriptions_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [push_subscriptions_user_id_fkey] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923135643_AddPushSubscriptions'
)
BEGIN
    CREATE INDEX [idx_push_subscriptions_user] ON [push_subscriptions] ([user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923135643_AddPushSubscriptions'
)
BEGIN
    CREATE INDEX [IX_push_subscriptions_org_id] ON [push_subscriptions] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923135643_AddPushSubscriptions'
)
BEGIN
    CREATE UNIQUE INDEX [push_subscriptions_endpoint_key] ON [push_subscriptions] ([endpoint]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923135643_AddPushSubscriptions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923135643_AddPushSubscriptions', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923160212_AddValueToOfferings'
)
BEGIN
    ALTER TABLE [offerings] ADD [value] decimal(12,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923160212_AddValueToOfferings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923160212_AddValueToOfferings', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924054024_AddDocuments'
)
BEGIN
    CREATE TABLE [documents] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NULL,
        [contract_id] uniqueidentifier NULL,
        [title] nvarchar(256) NOT NULL,
        [category] nvarchar(256) NULL,
        [notes] nvarchar(256) NULL,
        [file_name] nvarchar(256) NOT NULL,
        [blob_name] nvarchar(256) NOT NULL,
        [content_type] nvarchar(256) NOT NULL,
        [size_bytes] bigint NOT NULL,
        [uploaded_by_user_id] uniqueidentifier NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [documents_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [documents_contract_id_fkey] FOREIGN KEY ([contract_id]) REFERENCES [contracts] ([id]) ON DELETE SET NULL,
        CONSTRAINT [documents_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [documents_uploaded_by_user_id_fkey] FOREIGN KEY ([uploaded_by_user_id]) REFERENCES [users] ([id]) ON DELETE SET NULL,
        CONSTRAINT [documents_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924054024_AddDocuments'
)
BEGIN
    CREATE INDEX [idx_documents_contract] ON [documents] ([contract_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924054024_AddDocuments'
)
BEGIN
    CREATE INDEX [idx_documents_year] ON [documents] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924054024_AddDocuments'
)
BEGIN
    CREATE INDEX [IX_documents_org_id] ON [documents] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924054024_AddDocuments'
)
BEGIN
    CREATE INDEX [IX_documents_uploaded_by_user_id] ON [documents] ([uploaded_by_user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924054024_AddDocuments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924054024_AddDocuments', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924162259_AddOpeningBalances'
)
BEGIN
    ALTER TABLE [puja_years] ADD [opening_bank_balance] decimal(12,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924162259_AddOpeningBalances'
)
BEGIN
    ALTER TABLE [puja_years] ADD [opening_cash_balance] decimal(12,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924162259_AddOpeningBalances'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924162259_AddOpeningBalances', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924164038_AddNotifications'
)
BEGIN
    CREATE TABLE [notifications] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [title] nvarchar(256) NOT NULL,
        [body] nvarchar(256) NOT NULL,
        [url] nvarchar(256) NOT NULL,
        [kind] nvarchar(256) NOT NULL,
        [actor_user_id] uniqueidentifier NULL,
        [actor_name] nvarchar(256) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [notifications_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [notifications_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924164038_AddNotifications'
)
BEGIN
    CREATE INDEX [idx_notifications_org_created] ON [notifications] ([org_id], [created_at]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924164038_AddNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924164038_AddNotifications', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    CREATE TABLE [collection_payments] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [parent_type] nvarchar(256) NOT NULL,
        [parent_id] uniqueidentifier NOT NULL,
        [amount] decimal(12,2) NOT NULL,
        [paid_on] nvarchar(256) NULL,
        [mode] nvarchar(256) NULL,
        [recorded_by_user_id] uniqueidentifier NULL,
        [recorded_by_name] nvarchar(256) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [collection_payments_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [collection_payments_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [collection_payments_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    CREATE INDEX [idx_collection_payments_parent] ON [collection_payments] ([parent_type], [parent_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    CREATE INDEX [idx_collection_payments_year] ON [collection_payments] ([year_id], [org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    CREATE INDEX [IX_collection_payments_org_id] ON [collection_payments] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode, recorded_by_name, created_at)
    SELECT NEWID(), org_id, year_id, 'subscription', id, collected, collected_on, mode, 'Carried over', SYSUTCDATETIME()
    FROM subscriptions WHERE collected IS NOT NULL AND collected > 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode, recorded_by_name, created_at)
    SELECT NEWID(), org_id, year_id, 'ad_booking', id, collected, collected_on, mode, 'Carried over', SYSUTCDATETIME()
    FROM ad_bookings WHERE collected IS NOT NULL AND collected > 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    INSERT INTO collection_payments (id, org_id, year_id, parent_type, parent_id, amount, paid_on, mode, recorded_by_name, created_at)
    SELECT NEWID(), org_id, year_id, 'special_donation', id, collected, collected_on, mode, 'Carried over', SYSUTCDATETIME()
    FROM special_donations WHERE collected IS NOT NULL AND collected > 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924202421_AddCollectionPayments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924202421_AddCollectionPayments', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925081033_AddPujaCalendarDays'
)
BEGIN
    CREATE TABLE [puja_calendar_days] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [label] nvarchar(256) NOT NULL,
        [event_date] nvarchar(256) NULL,
        [note] nvarchar(256) NULL,
        [sort_order] int NOT NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [puja_calendar_days_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [puja_calendar_days_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [puja_calendar_days_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925081033_AddPujaCalendarDays'
)
BEGIN
    CREATE INDEX [idx_puja_calendar_days_year] ON [puja_calendar_days] ([year_id], [org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925081033_AddPujaCalendarDays'
)
BEGIN
    CREATE INDEX [IX_puja_calendar_days_org_id] ON [puja_calendar_days] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925081033_AddPujaCalendarDays'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925081033_AddPujaCalendarDays'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925081033_AddPujaCalendarDays', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092654_SurfaceOrphanBudgetCategories'
)
BEGIN
    DROP VIEW IF EXISTS v_budget_vs_actual;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092654_SurfaceOrphanBudgetCategories'
)
BEGIN
    CREATE VIEW v_budget_vs_actual AS
    SELECT
      b.id, b.org_id, b.year_id, b.category, b.notes, b.forecast,
      b.contract_amount, b.sort_order,
      COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0) AS actual,
      COALESCE(b.forecast, 0) - (COALESCE(e.actual, 0) + COALESCE(cc.unspent, 0)) AS remaining,
      COALESCE(pe.actual, 0) AS prior_actual,
      COALESCE(cc.total, 0) AS contracted
    FROM budget_lines b
    JOIN puja_years py ON py.id = b.year_id
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) e ON e.year_id = b.year_id AND e.category = b.category
    LEFT JOIN (
      SELECT c.year_id, c.category,
             SUM(c.contract_amount) AS total,
             CASE WHEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0) > 0
                  THEN SUM(c.contract_amount) - COALESCE(SUM(cp.paid), 0)
                  ELSE 0 END AS unspent
      FROM contracts c
      LEFT JOIN (
        SELECT contract_id, SUM(amount) AS paid FROM expenses WHERE contract_id IS NOT NULL GROUP BY contract_id
      ) cp ON cp.contract_id = c.id
      WHERE c.status <> 'cancelled'
      GROUP BY c.year_id, c.category
    ) cc ON cc.year_id = b.year_id AND cc.category = b.category
    LEFT JOIN puja_years ppy
      ON ppy.org_id = b.org_id
     AND ppy.year = (SELECT MAX(py2.year) FROM puja_years py2 WHERE py2.org_id = b.org_id AND py2.year < py.year)
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) pe ON pe.year_id = ppy.id AND pe.category = b.category

    UNION ALL

    -- A category can be spent against (Expenses lets anyone pick any
    -- expense_category lookup value) without anyone ever having created a
    -- matching budget_lines row. Before this, that money was invisible on
    -- the Budget page entirely — the view only ever started from
    -- budget_lines. Surfaced here with forecast=0 so real spend is never
    -- silently missing, sorted to the very end so set-up budget lines
    -- still lead the table.
    SELECT
      NEWID() AS id, py2b.org_id, ex.year_id, ex.category,
      N'No budget line set for this category — spend recorded, forecast not.' AS notes,
      0 AS forecast, 0 AS contract_amount, 2147483647 AS sort_order,
      ex.actual AS actual,
      -ex.actual AS remaining,
      COALESCE(pe2.actual, 0) AS prior_actual,
      0 AS contracted
    FROM (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses WHERE category IS NOT NULL GROUP BY year_id, category
    ) ex
    JOIN puja_years py2b ON py2b.id = ex.year_id
    LEFT JOIN puja_years ppy2
      ON ppy2.org_id = py2b.org_id
     AND ppy2.year = (SELECT MAX(py3.year) FROM puja_years py3 WHERE py3.org_id = py2b.org_id AND py3.year < py2b.year)
    LEFT JOIN (
      SELECT year_id, category, SUM(amount) AS actual
      FROM expenses GROUP BY year_id, category
    ) pe2 ON pe2.year_id = ppy2.id AND pe2.category = ex.category
    WHERE NOT EXISTS (
      SELECT 1 FROM budget_lines b2 WHERE b2.year_id = ex.year_id AND b2.category = ex.category
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925092654_SurfaceOrphanBudgetCategories'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925092654_SurfaceOrphanBudgetCategories', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926134342_AddApiKeys'
)
BEGIN
    CREATE TABLE [api_keys] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [name] nvarchar(128) NOT NULL,
        [key_hash] nvarchar(64) NOT NULL,
        [prefix] nvarchar(24) NOT NULL,
        [created_by_user_id] uniqueidentifier NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [last_used_at] datetime2 NULL,
        [revoked_at] datetime2 NULL,
        CONSTRAINT [api_keys_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [api_keys_created_by_user_id_fkey] FOREIGN KEY ([created_by_user_id]) REFERENCES [users] ([id]) ON DELETE SET NULL,
        CONSTRAINT [api_keys_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926134342_AddApiKeys'
)
BEGIN
    CREATE UNIQUE INDEX [api_keys_key_hash_key] ON [api_keys] ([key_hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926134342_AddApiKeys'
)
BEGIN
    CREATE INDEX [IX_api_keys_created_by_user_id] ON [api_keys] ([created_by_user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926134342_AddApiKeys'
)
BEGIN
    CREATE INDEX [IX_api_keys_org_id] ON [api_keys] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926134342_AddApiKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926134342_AddApiKeys', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154840_AddSubscriptionNotEligible'
)
BEGIN
    ALTER TABLE [subscriptions] ADD [not_eligible] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154840_AddSubscriptionNotEligible'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005154840_AddSubscriptionNotEligible', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013125343_AddVendorIssues'
)
BEGIN
    CREATE TABLE [vendor_issues] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [vendor_id] uniqueidentifier NOT NULL,
        [contract_id] uniqueidentifier NULL,
        [issue_type] nvarchar(256) NOT NULL,
        [description] nvarchar(256) NOT NULL,
        [occurred_on] nvarchar(256) NULL,
        [status] nvarchar(256) NOT NULL DEFAULT ('open'),
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [vendor_issues_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [vendor_issues_contract_id_fkey] FOREIGN KEY ([contract_id]) REFERENCES [contracts] ([id]) ON DELETE SET NULL,
        CONSTRAINT [vendor_issues_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [vendor_issues_vendor_id_fkey] FOREIGN KEY ([vendor_id]) REFERENCES [vendors] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013125343_AddVendorIssues'
)
BEGIN
    CREATE INDEX [idx_vendor_issues_contract] ON [vendor_issues] ([contract_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013125343_AddVendorIssues'
)
BEGIN
    CREATE INDEX [idx_vendor_issues_vendor] ON [vendor_issues] ([vendor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013125343_AddVendorIssues'
)
BEGIN
    CREATE INDEX [IX_vendor_issues_org_id] ON [vendor_issues] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013125343_AddVendorIssues'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013125343_AddVendorIssues', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    CREATE TABLE [feasts] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [donor_id] uniqueidentifier NOT NULL,
        [year_id] uniqueidentifier NOT NULL,
        [bill_no] nvarchar(256) NULL,
        [not_eligible] bit NOT NULL DEFAULT CAST(0 AS bit),
        [billed] decimal(12,2) NULL DEFAULT 0.0,
        [billed_on] nvarchar(256) NULL,
        [collected] decimal(12,2) NULL DEFAULT 0.0,
        [due] AS (COALESCE([billed], 0) - COALESCE([collected], 0)) PERSISTED,
        [mode] nvarchar(256) NULL,
        [collected_on] nvarchar(256) NULL,
        [collector_id] uniqueidentifier NULL,
        [comments] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [feasts_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [feasts_collector_id_fkey] FOREIGN KEY ([collector_id]) REFERENCES [collectors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [feasts_donor_id_fkey] FOREIGN KEY ([donor_id]) REFERENCES [donors] ([id]) ON DELETE CASCADE,
        CONSTRAINT [feasts_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [feasts_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    CREATE UNIQUE INDEX [feasts_donor_id_year_id_key] ON [feasts] ([donor_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    CREATE INDEX [idx_feast_org] ON [feasts] ([org_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    CREATE INDEX [idx_feast_year] ON [feasts] ([year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    CREATE INDEX [IX_feasts_collector_id] ON [feasts] ([collector_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    CREATE INDEX [IX_feasts_org_id_year_id] ON [feasts] ([org_id], [year_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    INSERT INTO lookups (id, org_id, category, label, sort_order, active)
    SELECT NEWID(), o.id, 'expense_category', 'Pritivoj',
           COALESCE((SELECT MAX(l.sort_order) FROM lookups l
                      WHERE l.org_id = o.id AND l.category = 'expense_category'), 0) + 1,
           1
      FROM organizations o
     WHERE NOT EXISTS (
         SELECT 1 FROM lookups l2
          WHERE l2.org_id = o.id AND l2.category = 'expense_category' AND l2.label = 'Pritivoj');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014123148_AddFeasts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014123148_AddFeasts', N'10.0.11');
END;

COMMIT;
GO

