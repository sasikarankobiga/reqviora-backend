START TRANSACTION;
CREATE TABLE "ProjectInvitations" (
    "Id" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "RecipientEmail" character varying(320) NOT NULL,
    "NormalizedRecipientEmail" character varying(320) NOT NULL,
    "Role" character varying(30) NOT NULL,
    "TokenHash" character varying(64) NOT NULL,
    "PendingRecipientKey" character varying(353),
    "Status" character varying(20) NOT NULL,
    "CreatedBy" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedAtUnixTimeMilliseconds" bigint NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "AcceptedBy" uuid,
    "AcceptedAt" timestamp with time zone,
    "RejectedBy" uuid,
    "RejectedAt" timestamp with time zone,
    "RevokedBy" uuid,
    "RevokedAt" timestamp with time zone,
    "SupersedesInvitationId" uuid,
    "ConcurrencyToken" uuid NOT NULL,
    CONSTRAINT "PK_ProjectInvitations" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ProjectInvitations_Role" CHECK ("Role" IN ('Viewer','Editor')),
    CONSTRAINT "CK_ProjectInvitations_Status" CHECK ("Status" IN ('Pending','Accepted','Rejected','Revoked','Expired')),
    CONSTRAINT "FK_ProjectInvitations_ProjectInvitations_SupersedesInvitationId" FOREIGN KEY ("SupersedesInvitationId") REFERENCES "ProjectInvitations" ("Id"),
    CONSTRAINT "FK_ProjectInvitations_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProjectInvitations_Users_AcceptedBy" FOREIGN KEY ("AcceptedBy") REFERENCES "Users" ("Id"),
    CONSTRAINT "FK_ProjectInvitations_Users_CreatedBy" FOREIGN KEY ("CreatedBy") REFERENCES "Users" ("Id"),
    CONSTRAINT "FK_ProjectInvitations_Users_RejectedBy" FOREIGN KEY ("RejectedBy") REFERENCES "Users" ("Id"),
    CONSTRAINT "FK_ProjectInvitations_Users_RevokedBy" FOREIGN KEY ("RevokedBy") REFERENCES "Users" ("Id")
);

CREATE INDEX "IX_ProjectInvitations_AcceptedBy" ON "ProjectInvitations" ("AcceptedBy");

CREATE INDEX "IX_ProjectInvitations_CreatedBy" ON "ProjectInvitations" ("CreatedBy");

CREATE INDEX "IX_ProjectInvitations_NormalizedRecipientEmail_Status" ON "ProjectInvitations" ("NormalizedRecipientEmail", "Status");

CREATE UNIQUE INDEX "IX_ProjectInvitations_PendingRecipientKey" ON "ProjectInvitations" ("PendingRecipientKey") WHERE "PendingRecipientKey" IS NOT NULL;

CREATE INDEX "IX_ProjectInvitations_ProjectId_CreatedAtUnixTimeMilliseconds" ON "ProjectInvitations" ("ProjectId", "CreatedAtUnixTimeMilliseconds");

CREATE INDEX "IX_ProjectInvitations_RejectedBy" ON "ProjectInvitations" ("RejectedBy");

CREATE INDEX "IX_ProjectInvitations_RevokedBy" ON "ProjectInvitations" ("RevokedBy");

CREATE INDEX "IX_ProjectInvitations_SupersedesInvitationId" ON "ProjectInvitations" ("SupersedesInvitationId");

CREATE UNIQUE INDEX "IX_ProjectInvitations_TokenHash" ON "ProjectInvitations" ("TokenHash");

ALTER TABLE public."ProjectInvitations" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public."ProjectInvitations" FROM PUBLIC;
DO $private_table$
DECLARE role_name text;
BEGIN
    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
        WHERE rolname IN ('anon', 'authenticated', 'service_role')
    LOOP
        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'ProjectInvitations', role_name);
    END LOOP;
END;
$private_table$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126061555_AddProjectInvitationLifecycle', '10.0.15');

COMMIT;
