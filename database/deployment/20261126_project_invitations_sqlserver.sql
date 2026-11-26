BEGIN TRANSACTION;
CREATE TABLE [ProjectInvitations] (
    [Id] uniqueidentifier NOT NULL,
    [ProjectId] uniqueidentifier NOT NULL,
    [RecipientEmail] nvarchar(320) NOT NULL,
    [NormalizedRecipientEmail] nvarchar(320) NOT NULL,
    [Role] nvarchar(30) NOT NULL,
    [TokenHash] varchar(64) NOT NULL,
    [PendingRecipientKey] nvarchar(353) NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedAtUnixTimeMilliseconds] bigint NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [AcceptedBy] uniqueidentifier NULL,
    [AcceptedAt] datetimeoffset NULL,
    [RejectedBy] uniqueidentifier NULL,
    [RejectedAt] datetimeoffset NULL,
    [RevokedBy] uniqueidentifier NULL,
    [RevokedAt] datetimeoffset NULL,
    [SupersedesInvitationId] uniqueidentifier NULL,
    [ConcurrencyToken] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ProjectInvitations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProjectInvitations_Role] CHECK ([Role] IN ('Viewer','Editor')),
    CONSTRAINT [CK_ProjectInvitations_Status] CHECK ([Status] IN ('Pending','Accepted','Rejected','Revoked','Expired')),
    CONSTRAINT [FK_ProjectInvitations_ProjectInvitations_SupersedesInvitationId] FOREIGN KEY ([SupersedesInvitationId]) REFERENCES [ProjectInvitations] ([Id]),
    CONSTRAINT [FK_ProjectInvitations_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ProjectInvitations_Users_AcceptedBy] FOREIGN KEY ([AcceptedBy]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_ProjectInvitations_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_ProjectInvitations_Users_RejectedBy] FOREIGN KEY ([RejectedBy]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_ProjectInvitations_Users_RevokedBy] FOREIGN KEY ([RevokedBy]) REFERENCES [Users] ([Id])
);

CREATE INDEX [IX_ProjectInvitations_AcceptedBy] ON [ProjectInvitations] ([AcceptedBy]);

CREATE INDEX [IX_ProjectInvitations_CreatedBy] ON [ProjectInvitations] ([CreatedBy]);

CREATE INDEX [IX_ProjectInvitations_NormalizedRecipientEmail_Status] ON [ProjectInvitations] ([NormalizedRecipientEmail], [Status]);

CREATE UNIQUE INDEX [IX_ProjectInvitations_PendingRecipientKey] ON [ProjectInvitations] ([PendingRecipientKey]) WHERE [PendingRecipientKey] IS NOT NULL;

CREATE INDEX [IX_ProjectInvitations_ProjectId_CreatedAtUnixTimeMilliseconds] ON [ProjectInvitations] ([ProjectId], [CreatedAtUnixTimeMilliseconds]);

CREATE INDEX [IX_ProjectInvitations_RejectedBy] ON [ProjectInvitations] ([RejectedBy]);

CREATE INDEX [IX_ProjectInvitations_RevokedBy] ON [ProjectInvitations] ([RevokedBy]);

CREATE INDEX [IX_ProjectInvitations_SupersedesInvitationId] ON [ProjectInvitations] ([SupersedesInvitationId]);

CREATE UNIQUE INDEX [IX_ProjectInvitations_TokenHash] ON [ProjectInvitations] ([TokenHash]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261126061607_AddProjectInvitationLifecycle', N'10.0.15');

COMMIT;
GO
