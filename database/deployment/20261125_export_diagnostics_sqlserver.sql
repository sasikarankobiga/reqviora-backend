BEGIN TRANSACTION;
CREATE TABLE [ExportJobDiagnostics] (
    [Id] uniqueidentifier NOT NULL,
    [ProjectId] uniqueidentifier NOT NULL,
    [ApprovedRevisionId] uniqueidentifier NOT NULL,
    [ExportRecordId] uniqueidentifier NULL,
    [ExportType] nvarchar(50) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [StartedAt] datetimeoffset NOT NULL,
    [CompletedAt] datetimeoffset NULL,
    [DurationMilliseconds] bigint NULL,
    [FailureCode] nvarchar(100) NULL,
    [FailureCategory] nvarchar(50) NULL,
    [IntegrityStatus] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_ExportJobDiagnostics] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ExportJobDiagnostics_ExportRecords_ExportRecordId] FOREIGN KEY ([ExportRecordId]) REFERENCES [ExportRecords] ([Id]),
    CONSTRAINT [FK_ExportJobDiagnostics_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id])
);

CREATE INDEX [IX_ExportJobDiagnostics_ExportRecordId] ON [ExportJobDiagnostics] ([ExportRecordId]);

CREATE INDEX [IX_ExportJobDiagnostics_ProjectId_CreatedAt] ON [ExportJobDiagnostics] ([ProjectId], [CreatedAt]);

CREATE INDEX [IX_ExportJobDiagnostics_Status_CreatedAt] ON [ExportJobDiagnostics] ([Status], [CreatedAt]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261125122210_AddExportJobDiagnostics', N'10.0.15');

COMMIT;
GO
