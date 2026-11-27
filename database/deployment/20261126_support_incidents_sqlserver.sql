BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE TABLE [SupportIncidents] (
        [Id] uniqueidentifier NOT NULL,
        [Reference] nvarchar(30) NOT NULL,
        [Category] nvarchar(80) NOT NULL,
        [Priority] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ReporterId] uniqueidentifier NOT NULL,
        [AssignedAdminId] uniqueidentifier NULL,
        [CorrelationId] nvarchar(100) NULL,
        [ResolutionNotes] nvarchar(1000) NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedAtUnixTimeMilliseconds] bigint NOT NULL,
        [ResolvedAt] datetimeoffset NULL,
        [ClosedAt] datetimeoffset NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [LastModifiedAt] datetimeoffset NULL,
        [LastModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_SupportIncidents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupportIncidents_Users_AssignedAdminId] FOREIGN KEY ([AssignedAdminId]) REFERENCES [Users] ([Id]),
        CONSTRAINT [FK_SupportIncidents_Users_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE TABLE [SupportIncidentHistory] (
        [Id] uniqueidentifier NOT NULL,
        [IncidentId] uniqueidentifier NOT NULL,
        [Action] nvarchar(40) NOT NULL,
        [PreviousValue] nvarchar(100) NULL,
        [NewValue] nvarchar(100) NULL,
        [Notes] nvarchar(1000) NULL,
        [ChangedBy] uniqueidentifier NOT NULL,
        [ChangedAt] datetimeoffset NOT NULL,
        [ChangedAtUnixTimeMilliseconds] bigint NOT NULL,
        CONSTRAINT [PK_SupportIncidentHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupportIncidentHistory_SupportIncidents_IncidentId] FOREIGN KEY ([IncidentId]) REFERENCES [SupportIncidents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SupportIncidentHistory_Users_ChangedBy] FOREIGN KEY ([ChangedBy]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE INDEX [IX_SupportIncidentHistory_ChangedBy] ON [SupportIncidentHistory] ([ChangedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE INDEX [IX_SupportIncidentHistory_IncidentId_ChangedAtUnixTimeMilliseconds] ON [SupportIncidentHistory] ([IncidentId], [ChangedAtUnixTimeMilliseconds]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE INDEX [IX_SupportIncidents_AssignedAdminId] ON [SupportIncidents] ([AssignedAdminId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupportIncidents_Reference] ON [SupportIncidents] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE INDEX [IX_SupportIncidents_ReporterId] ON [SupportIncidents] ([ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    CREATE INDEX [IX_SupportIncidents_Status_Priority_UpdatedAtUnixTimeMilliseconds] ON [SupportIncidents] ([Status], [Priority], [UpdatedAtUnixTimeMilliseconds]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126053539_AddSupportIncidentManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261126053539_AddSupportIncidentManagement', N'10.0.15');
END;

COMMIT;
GO
