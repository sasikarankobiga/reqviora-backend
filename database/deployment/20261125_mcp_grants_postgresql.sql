START TRANSACTION;
CREATE TABLE "McpAccessGrants" (
    "Id" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "ApprovedRevisionId" uuid NOT NULL,
    "OwnerId" uuid NOT NULL,
    "SecretHash" character varying(64) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "RevokedBy" uuid,
    "RevocationReason" character varying(500),
    "ConcurrencyToken" uuid NOT NULL,
    CONSTRAINT "PK_McpAccessGrants" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_McpAccessGrants_ApprovedRevisions_ApprovedRevisionId_Projec~" FOREIGN KEY ("ApprovedRevisionId", "ProjectId") REFERENCES "ApprovedRevisions" ("Id", "ProjectId"),
    CONSTRAINT "FK_McpAccessGrants_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id"),
    CONSTRAINT "FK_McpAccessGrants_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id"),
    CONSTRAINT "FK_McpAccessGrants_Users_RevokedBy" FOREIGN KEY ("RevokedBy") REFERENCES "Users" ("Id")
);

CREATE INDEX "IX_McpAccessGrants_ApprovedRevisionId_ProjectId" ON "McpAccessGrants" ("ApprovedRevisionId", "ProjectId");

CREATE INDEX "IX_McpAccessGrants_OwnerId" ON "McpAccessGrants" ("OwnerId");

CREATE INDEX "IX_McpAccessGrants_ProjectId_CreatedAt" ON "McpAccessGrants" ("ProjectId", "CreatedAt");

CREATE INDEX "IX_McpAccessGrants_RevokedAt_ExpiresAt" ON "McpAccessGrants" ("RevokedAt", "ExpiresAt");

CREATE INDEX "IX_McpAccessGrants_RevokedBy" ON "McpAccessGrants" ("RevokedBy");

CREATE UNIQUE INDEX "IX_McpAccessGrants_SecretHash" ON "McpAccessGrants" ("SecretHash");

ALTER TABLE public."McpAccessGrants" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public."McpAccessGrants" FROM PUBLIC;
DO $private_table$
DECLARE role_name text;
BEGIN
    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
        WHERE rolname IN ('anon', 'authenticated', 'service_role')
    LOOP
        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'McpAccessGrants', role_name);
    END LOOP;
END;
$private_table$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261125120753_CompleteMcpGrantLifecycle', '10.0.15');

COMMIT;
