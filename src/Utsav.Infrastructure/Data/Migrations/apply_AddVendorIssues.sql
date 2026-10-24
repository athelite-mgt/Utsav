-- Apply the AddVendorIssues migration to Azure SQL.
-- Safe to re-run: every statement is guarded against already having applied.
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

COMMIT;
