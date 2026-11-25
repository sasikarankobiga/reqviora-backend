BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125062130_SetEmailVerifiedDefaultFalse'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'EmailVerified');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Users] ADD DEFAULT CAST(0 AS bit) FOR [EmailVerified];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125062130_SetEmailVerifiedDefaultFalse'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125062130_SetEmailVerifiedDefaultFalse', N'10.0.15');
END;

COMMIT;
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063346_AddUserSessionVersion'
)
BEGIN
    ALTER TABLE [Users] ADD [SessionVersion] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125063346_AddUserSessionVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125063346_AddUserSessionVersion', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085009_AddPersistentAiJobs'
)
BEGIN
    CREATE TABLE [AiJobs] (
        [Id] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [Prompt] nvarchar(max) NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [State] nvarchar(20) NOT NULL,
        [Result] nvarchar(max) NULL,
        [ErrorCode] nvarchar(100) NULL,
        [ErrorMessage] nvarchar(500) NULL,
        [AttemptCount] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [TimeoutSeconds] int NOT NULL,
        [AvailableAtUnixTimeMilliseconds] bigint NOT NULL,
        [LeaseOwner] nvarchar(300) NULL,
        [LeaseExpiresAtUnixTimeMilliseconds] bigint NULL,
        [StartedAt] datetimeoffset NULL,
        [CompletedAt] datetimeoffset NULL,
        [CancellationRequestedAt] datetimeoffset NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [LastModifiedAt] datetimeoffset NULL,
        [LastModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_AiJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AiJobs_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AiJobs_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085009_AddPersistentAiJobs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiJobs_OwnerId_ProjectId_IdempotencyKey] ON [AiJobs] ([OwnerId], [ProjectId], [IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085009_AddPersistentAiJobs'
)
BEGIN
    CREATE INDEX [IX_AiJobs_ProjectId] ON [AiJobs] ([ProjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085009_AddPersistentAiJobs'
)
BEGIN
    CREATE INDEX [IX_AiJobs_State_AvailableAtUnixTimeMilliseconds_LeaseExpiresAtUnixTimeMilliseconds] ON [AiJobs] ([State], [AvailableAtUnixTimeMilliseconds], [LeaseExpiresAtUnixTimeMilliseconds]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125085009_AddPersistentAiJobs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125085009_AddPersistentAiJobs', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [ConcurrencyToken] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [MediaType] nvarchar(150) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [ParseErrorCode] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [ParseLeaseExpiresAtUnixTimeMilliseconds] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [ParsedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [ParsingStatus] nvarchar(30) NOT NULL DEFAULT N'pending';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [ParsingWarningsJson] nvarchar(max) NOT NULL DEFAULT N'[]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [Sha256] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    ALTER TABLE [ProjectDocuments] ADD [SizeBytes] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    CREATE TABLE [ProjectDocumentSegments] (
        [Id] uniqueidentifier NOT NULL,
        [ProjectDocumentId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [Locator] nvarchar(500) NOT NULL,
        [PageNumber] int NULL,
        [StartOffset] int NOT NULL,
        [EndOffset] int NOT NULL,
        CONSTRAINT [PK_ProjectDocumentSegments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjectDocumentSegments_ProjectDocuments_ProjectDocumentId] FOREIGN KEY ([ProjectDocumentId]) REFERENCES [ProjectDocuments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    CREATE INDEX [IX_ProjectDocuments_ProjectId_ParsingStatus] ON [ProjectDocuments] ([ProjectId], [ParsingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProjectDocuments_ProjectId_Sha256] ON [ProjectDocuments] ([ProjectId], [Sha256]) WHERE [Sha256] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProjectDocumentSegments_ProjectDocumentId_Sequence] ON [ProjectDocumentSegments] ([ProjectDocumentId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261125090548_PersistDocumentIntelligence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261125090548_PersistDocumentIntelligence', N'10.0.15');
END;

COMMIT;
GO
-- End of reviewed pending SQL Server migrations.
