BEGIN TRANSACTION;
CREATE TABLE [McpAccessGrants] (
    [Id] uniqueidentifier NOT NULL,
    [ProjectId] uniqueidentifier NOT NULL,
    [ApprovedRevisionId] uniqueidentifier NOT NULL,
    [OwnerId] uniqueidentifier NOT NULL,
    [SecretHash] varchar(64) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [RevokedAt] datetimeoffset NULL,
    [RevokedBy] uniqueidentifier NULL,
    [RevocationReason] nvarchar(500) NULL,
    [ConcurrencyToken] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_McpAccessGrants] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_McpAccessGrants_ApprovedRevisions_ApprovedRevisionId_ProjectId] FOREIGN KEY ([ApprovedRevisionId], [ProjectId]) REFERENCES [ApprovedRevisions] ([Id], [ProjectId]),
    CONSTRAINT [FK_McpAccessGrants_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]),
    CONSTRAINT [FK_McpAccessGrants_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_McpAccessGrants_Users_RevokedBy] FOREIGN KEY ([RevokedBy]) REFERENCES [Users] ([Id])
);

CREATE INDEX [IX_McpAccessGrants_ApprovedRevisionId_ProjectId] ON [McpAccessGrants] ([ApprovedRevisionId], [ProjectId]);

CREATE INDEX [IX_McpAccessGrants_OwnerId] ON [McpAccessGrants] ([OwnerId]);

CREATE INDEX [IX_McpAccessGrants_ProjectId_CreatedAt] ON [McpAccessGrants] ([ProjectId], [CreatedAt]);

CREATE INDEX [IX_McpAccessGrants_RevokedAt_ExpiresAt] ON [McpAccessGrants] ([RevokedAt], [ExpiresAt]);

CREATE INDEX [IX_McpAccessGrants_RevokedBy] ON [McpAccessGrants] ([RevokedBy]);

CREATE UNIQUE INDEX [IX_McpAccessGrants_SecretHash] ON [McpAccessGrants] ([SecretHash]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261125120817_CompleteMcpGrantLifecycle', N'10.0.15');

COMMIT;
GO
