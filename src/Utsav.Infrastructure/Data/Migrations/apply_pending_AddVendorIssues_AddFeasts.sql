-- Creates the two tables that were never actually applied to production:
-- vendor_issues and feasts. Confirmed via a read-only check against the live
-- database that neither currently exists and that __EFMigrationsHistory's
-- last real entry is AddSubscriptionNotEligible — both are purely additive,
-- nothing is dropped or modified, and both guards make this safe to re-run.

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

    CREATE INDEX [idx_vendor_issues_contract] ON [vendor_issues] ([contract_id]);
    CREATE INDEX [idx_vendor_issues_vendor] ON [vendor_issues] ([vendor_id]);
    CREATE INDEX [IX_vendor_issues_org_id] ON [vendor_issues] ([org_id]);

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013125343_AddVendorIssues', N'10.0.11');
END;

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

    CREATE UNIQUE INDEX [feasts_donor_id_year_id_key] ON [feasts] ([donor_id], [year_id]);
    CREATE INDEX [idx_feast_org] ON [feasts] ([org_id]);
    CREATE INDEX [idx_feast_year] ON [feasts] ([year_id]);
    CREATE INDEX [IX_feasts_collector_id] ON [feasts] ([collector_id]);
    CREATE INDEX [IX_feasts_org_id_year_id] ON [feasts] ([org_id], [year_id]);

    INSERT INTO lookups (id, org_id, category, label, sort_order, active)
    SELECT NEWID(), o.id, 'expense_category', 'Pritivoj',
           COALESCE((SELECT MAX(l.sort_order) FROM lookups l
                      WHERE l.org_id = o.id AND l.category = 'expense_category'), 0) + 1,
           1
      FROM organizations o
     WHERE NOT EXISTS (
         SELECT 1 FROM lookups l2
          WHERE l2.org_id = o.id AND l2.category = 'expense_category' AND l2.label = 'Pritivoj');

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014123148_AddFeasts', N'10.0.11');
END;

COMMIT;

-- Sanity check — both should list their full column sets.
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_NAME IN ('feasts', 'vendor_issues')
 ORDER BY TABLE_NAME, ORDINAL_POSITION;
