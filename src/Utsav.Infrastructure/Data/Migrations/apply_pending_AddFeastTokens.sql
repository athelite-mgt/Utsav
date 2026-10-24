-- Creates the feast_tokens table (QR entry-token system) in production.
-- Purely additive — a brand new table, nothing dropped or modified. Guarded
-- on __EFMigrationsHistory so it is safe to re-run if it partially applied.
--
-- Note the feast_id foreign key has no ON DELETE action (NO ACTION): SQL
-- Server refuses a second cascade path from puja_years to feast_tokens
-- (direct via year_id, and indirect via puja_years -> feasts -> feast_tokens
-- if feast_id also cascaded/set-nulled). The API detaches feast_tokens.feast_id
-- explicitly before a feast delete goes through instead — see
-- TableRegistry.DetachBeforeDelete in the application code.

BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018134143_AddFeastTokens'
)
BEGIN
    CREATE TABLE [feast_tokens] (
        [id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [org_id] uniqueidentifier NULL,
        [year_id] uniqueidentifier NOT NULL,
        [seq_no] int NOT NULL,
        [code] nvarchar(32) NOT NULL,
        [feast_id] uniqueidentifier NULL,
        [status] nvarchar(16) NOT NULL DEFAULT ('unassigned'),
        [assigned_at] datetime2 NULL,
        [redeemed_at] datetime2 NULL,
        [redeemed_by_user_id] uniqueidentifier NULL,
        [redeemed_by_name] nvarchar(256) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [feast_tokens_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [feast_tokens_feast_id_fkey] FOREIGN KEY ([feast_id]) REFERENCES [feasts] ([id]),
        CONSTRAINT [feast_tokens_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [feast_tokens_redeemed_by_user_id_fkey] FOREIGN KEY ([redeemed_by_user_id]) REFERENCES [users] ([id]) ON DELETE SET NULL,
        CONSTRAINT [feast_tokens_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [feast_tokens_code_key] ON [feast_tokens] ([code]);
    CREATE INDEX [idx_feast_tokens_feast] ON [feast_tokens] ([feast_id]);
    CREATE INDEX [idx_feast_tokens_org] ON [feast_tokens] ([org_id]);
    CREATE INDEX [idx_feast_tokens_status] ON [feast_tokens] ([status]);
    CREATE INDEX [idx_feast_tokens_year] ON [feast_tokens] ([year_id]);
    CREATE INDEX [IX_feast_tokens_org_id_year_id] ON [feast_tokens] ([org_id], [year_id]);
    CREATE INDEX [IX_feast_tokens_redeemed_by_user_id] ON [feast_tokens] ([redeemed_by_user_id]);

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261018134143_AddFeastTokens', N'10.0.11');
END;

COMMIT;

-- Sanity check — should list all 12 columns.
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_NAME = 'feast_tokens'
 ORDER BY ORDINAL_POSITION;
