BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126052418_AddPlatformSettingVersions'
)
BEGIN
    CREATE TABLE [PlatformSettingRevisions] (
        [Key] nvarchar(80) NOT NULL,
        [Revision] int NOT NULL,
        [PreviousValue] bigint NOT NULL,
        [Value] bigint NOT NULL,
        [ChangedBy] uniqueidentifier NOT NULL,
        [ChangedAt] datetimeoffset NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [RolledBackTo] int NULL,
        CONSTRAINT [PK_PlatformSettingRevisions] PRIMARY KEY ([Key], [Revision]),
        CONSTRAINT [FK_PlatformSettingRevisions_Users_ChangedBy] FOREIGN KEY ([ChangedBy]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126052418_AddPlatformSettingVersions'
)
BEGIN
    CREATE INDEX [IX_PlatformSettingRevisions_ChangedBy] ON [PlatformSettingRevisions] ([ChangedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126052418_AddPlatformSettingVersions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261126052418_AddPlatformSettingVersions', N'10.0.15');
END;

COMMIT;
GO
