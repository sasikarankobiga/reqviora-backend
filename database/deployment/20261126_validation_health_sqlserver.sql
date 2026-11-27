BEGIN TRANSACTION;
CREATE TABLE [ValidationTelemetry] (
    [Id] uniqueidentifier NOT NULL,
    [CompletedAtMilliseconds] bigint NOT NULL,
    [UtcDay] int NOT NULL,
    [Workflow] nvarchar(16) NOT NULL,
    [Passed] bit NOT NULL,
    [DurationMilliseconds] bigint NOT NULL,
    [SchemaFailures] int NOT NULL,
    [BrokenReferences] int NOT NULL,
    [UnsupportedComponents] int NOT NULL,
    [NavigationFailures] int NOT NULL,
    [OtherFailures] int NOT NULL,
    CONSTRAINT [PK_ValidationTelemetry] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_ValidationTelemetry_UtcDay] ON [ValidationTelemetry] ([UtcDay]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261126051540_AddValidationHealth', N'10.0.15');

COMMIT;
GO
