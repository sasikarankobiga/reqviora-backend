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

-- End of REQVIORA pending PostgreSQL deployment script.
