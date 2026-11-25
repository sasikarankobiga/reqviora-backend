START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125062108_SetEmailVerifiedDefaultFalse') THEN
    ALTER TABLE "Users" ALTER COLUMN "EmailVerified" SET DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125062108_SetEmailVerifiedDefaultFalse') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125062108_SetEmailVerifiedDefaultFalse', '10.0.15');
    END IF;
END $EF$;
COMMIT;
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125063334_AddUserSessionVersion') THEN
    ALTER TABLE "Users" ADD "SessionVersion" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125063334_AddUserSessionVersion') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125063334_AddUserSessionVersion', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125085005_AddPersistentAiJobs') THEN
    CREATE TABLE "AiJobs" (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "OwnerId" uuid NOT NULL,
        "Prompt" text NOT NULL,
        "IdempotencyKey" character varying(128) NOT NULL,
        "State" character varying(20) NOT NULL,
        "Result" text,
        "ErrorCode" character varying(100),
        "ErrorMessage" character varying(500),
        "AttemptCount" integer NOT NULL,
        "MaxAttempts" integer NOT NULL,
        "TimeoutSeconds" integer NOT NULL,
        "AvailableAtUnixTimeMilliseconds" bigint NOT NULL,
        "LeaseOwner" character varying(300),
        "LeaseExpiresAtUnixTimeMilliseconds" bigint,
        "StartedAt" timestamp with time zone,
        "CompletedAt" timestamp with time zone,
        "CancellationRequestedAt" timestamp with time zone,
        "ConcurrencyToken" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "LastModifiedAt" timestamp with time zone,
        "LastModifiedBy" text,
        CONSTRAINT "PK_AiJobs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiJobs_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_AiJobs_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125085005_AddPersistentAiJobs') THEN
    CREATE UNIQUE INDEX "IX_AiJobs_OwnerId_ProjectId_IdempotencyKey" ON "AiJobs" ("OwnerId", "ProjectId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125085005_AddPersistentAiJobs') THEN
    CREATE INDEX "IX_AiJobs_ProjectId" ON "AiJobs" ("ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125085005_AddPersistentAiJobs') THEN
    CREATE INDEX "IX_AiJobs_State_AvailableAtUnixTimeMilliseconds_LeaseExpiresAt~" ON "AiJobs" ("State", "AvailableAtUnixTimeMilliseconds", "LeaseExpiresAtUnixTimeMilliseconds");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125085005_AddPersistentAiJobs') THEN
    ALTER TABLE public."AiJobs" ENABLE ROW LEVEL SECURITY;
    REVOKE ALL ON TABLE public."AiJobs" FROM PUBLIC;
    DO $private_ai_jobs$
    DECLARE role_name text;
    BEGIN
        FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
            WHERE rolname IN ('anon', 'authenticated', 'service_role')
        LOOP
            EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'AiJobs', role_name);
        END LOOP;
    END;
    $private_ai_jobs$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125085005_AddPersistentAiJobs') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125085005_AddPersistentAiJobs', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "ConcurrencyToken" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "MediaType" character varying(150) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "ParseErrorCode" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "ParseLeaseExpiresAtUnixTimeMilliseconds" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "ParsedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "ParsingStatus" character varying(30) NOT NULL DEFAULT 'pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "ParsingWarningsJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "Sha256" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE "ProjectDocuments" ADD "SizeBytes" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    CREATE TABLE "ProjectDocumentSegments" (
        "Id" uuid NOT NULL,
        "ProjectDocumentId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Text" text NOT NULL,
        "SourceId" character varying(100) NOT NULL,
        "Locator" character varying(500) NOT NULL,
        "PageNumber" integer,
        "StartOffset" integer NOT NULL,
        "EndOffset" integer NOT NULL,
        CONSTRAINT "PK_ProjectDocumentSegments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProjectDocumentSegments_ProjectDocuments_ProjectDocumentId" FOREIGN KEY ("ProjectDocumentId") REFERENCES "ProjectDocuments" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    CREATE INDEX "IX_ProjectDocuments_ProjectId_ParsingStatus" ON "ProjectDocuments" ("ProjectId", "ParsingStatus");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    CREATE UNIQUE INDEX "IX_ProjectDocuments_ProjectId_Sha256" ON "ProjectDocuments" ("ProjectId", "Sha256") WHERE "Sha256" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    CREATE UNIQUE INDEX "IX_ProjectDocumentSegments_ProjectDocumentId_Sequence" ON "ProjectDocumentSegments" ("ProjectDocumentId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    ALTER TABLE public."ProjectDocumentSegments" ENABLE ROW LEVEL SECURITY;
    REVOKE ALL ON TABLE public."ProjectDocumentSegments" FROM PUBLIC;
    DO $private_document_segments$
    DECLARE role_name text;
    BEGIN
        FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
            WHERE rolname IN ('anon', 'authenticated', 'service_role')
        LOOP
            EXECUTE format(
                'REVOKE ALL ON TABLE public.%I FROM %I',
                'ProjectDocumentSegments', role_name);
        END LOOP;
    END;
    $private_document_segments$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125090542_PersistDocumentIntelligence') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125090542_PersistDocumentIntelligence', '10.0.15');
    END IF;
END $EF$;
COMMIT;
-- End of reviewed pending PostgreSQL migrations.
