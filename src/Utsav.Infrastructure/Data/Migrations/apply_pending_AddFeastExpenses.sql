-- Creates the feast_expenses table in production — spending against the
-- feast pool (catering, decoration, prasad), kept in its own table so it
-- never touches the general committee's Budget/Expenses/Fund Position
-- figures, same reasoning as feasts/feast_tokens. Purely additive. Guarded
-- on __EFMigrationsHistory so it is safe to re-run if it partially applied.

BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019012046_AddFeastExpenses'
)
BEGIN
    CREATE TABLE [feast_expenses] (
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
        [bill_no] nvarchar(256) NULL,
        [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [feast_expenses_pkey] PRIMARY KEY ([id]),
        CONSTRAINT [feast_expenses_org_id_fkey] FOREIGN KEY ([org_id]) REFERENCES [organizations] ([id]),
        CONSTRAINT [feast_expenses_vendor_id_fkey] FOREIGN KEY ([vendor_id]) REFERENCES [vendors] ([id]) ON DELETE SET NULL,
        CONSTRAINT [feast_expenses_year_id_fkey] FOREIGN KEY ([year_id]) REFERENCES [puja_years] ([id]) ON DELETE CASCADE
    );

    CREATE INDEX [idx_feast_expenses_org] ON [feast_expenses] ([org_id]);
    CREATE INDEX [idx_feast_expenses_year] ON [feast_expenses] ([year_id]);
    CREATE INDEX [IX_feast_expenses_org_id_year_id_category] ON [feast_expenses] ([org_id], [year_id], [category]);
    CREATE INDEX [IX_feast_expenses_vendor_id] ON [feast_expenses] ([vendor_id]);

    -- A handful of sensible starting categories so the dropdown isn't empty
    -- on first use — the committee can add/edit/remove these from Settings
    -- like any other lookup category.
    INSERT INTO lookups (id, org_id, category, label, sort_order, active)
    SELECT NEWID(), o.id, 'feast_expense_category', v.label, v.sort_order, 1
      FROM organizations o
      CROSS JOIN (VALUES ('Catering', 1), ('Decoration', 2), ('Prasad', 3), ('Transport', 4), ('Pritivoj', 5), ('Miscellaneous', 6)) AS v(label, sort_order)
     WHERE NOT EXISTS (
         SELECT 1 FROM lookups l2
          WHERE l2.org_id = o.id AND l2.category = 'feast_expense_category' AND l2.label = v.label);

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019012046_AddFeastExpenses', N'10.0.11');
END;

COMMIT;

-- Sanity check — should list all 11 columns.
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_NAME = 'feast_expenses'
 ORDER BY ORDINAL_POSITION;
