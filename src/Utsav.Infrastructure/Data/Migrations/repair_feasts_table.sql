-- Repair script for a broken/incomplete `feasts` table in production.
--
-- Symptom: every read AND write against `feasts` throws a 500, including the
-- simplest possible query (a plain `SELECT *` / generic INSERT), while every
-- other table using the exact same code path works fine. That points at the
-- table itself being malformed in production, not application code — most
-- likely the original apply_AddFeasts.sql run recorded the migration as
-- applied in __EFMigrationsHistory without the table actually finishing
-- creation correctly (e.g. a dropped connection mid-script).
--
-- Confirmed empty before running this: the Feast page shows 0 donors / ₹0
-- everywhere, and the History audit log shows no Feast or Donor entries.
-- Safe to drop and recreate — nothing is lost.
--
-- Run this against the production Azure SQL database (Azure Data Studio /
-- SSMS / Portal Query Editor), then reload the Feast page.

IF OBJECT_ID('dbo.feasts', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.feasts;
END

DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261014123148_AddFeasts';

-- Also make sure the Pritivoj lookup exists (idempotent — same guard as the
-- original migration, re-run here in case the table drop above also lost
-- confidence in whether this ran).
IF NOT EXISTS (
    SELECT 1 FROM lookups l2
     WHERE l2.category = 'expense_category' AND l2.label = 'Pritivoj')
BEGIN
    INSERT INTO lookups (id, org_id, category, label, sort_order, active)
    SELECT NEWID(), o.id, 'expense_category', 'Pritivoj',
           COALESCE((SELECT MAX(l.sort_order) FROM lookups l
                      WHERE l.org_id = o.id AND l.category = 'expense_category'), 0) + 1,
           1
      FROM organizations o;
END
GO

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
GO

CREATE UNIQUE INDEX [feasts_donor_id_year_id_key] ON [feasts] ([donor_id], [year_id]);
CREATE INDEX [idx_feast_org] ON [feasts] ([org_id]);
CREATE INDEX [idx_feast_year] ON [feasts] ([year_id]);
CREATE INDEX [IX_feasts_collector_id] ON [feasts] ([collector_id]);
CREATE INDEX [IX_feasts_org_id_year_id] ON [feasts] ([org_id], [year_id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261014123148_AddFeasts', N'10.0.11');
GO

-- Sanity check: confirm the table now has exactly the expected 16 columns.
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_NAME = 'feasts'
 ORDER BY ORDINAL_POSITION;
