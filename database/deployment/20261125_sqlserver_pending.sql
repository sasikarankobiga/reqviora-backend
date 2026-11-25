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

-- End of REQVIORA pending SQL Server deployment script.
