BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125113817_AddUserAdministrationState'
)
BEGIN
    ALTER TABLE [Users] ADD [DisabledAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125113817_AddUserAdministrationState'
)
BEGIN
    ALTER TABLE [Users] ADD [DisabledReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125113817_AddUserAdministrationState'
)
BEGIN
    ALTER TABLE [Users] ADD [IsDisabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125113817_AddUserAdministrationState'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125113817_AddUserAdministrationState', N'10.0.15');
END;

COMMIT;
GO

