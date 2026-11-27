START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126052414_AddPlatformSettingVersions') THEN
    CREATE TABLE "PlatformSettingRevisions" (
        "Key" character varying(80) NOT NULL,
        "Revision" integer NOT NULL,
        "PreviousValue" bigint NOT NULL,
        "Value" bigint NOT NULL,
        "ChangedBy" uuid NOT NULL,
        "ChangedAt" timestamp with time zone NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "RolledBackTo" integer,
        CONSTRAINT "PK_PlatformSettingRevisions" PRIMARY KEY ("Key", "Revision"),
        CONSTRAINT "FK_PlatformSettingRevisions_Users_ChangedBy" FOREIGN KEY ("ChangedBy") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126052414_AddPlatformSettingVersions') THEN
    CREATE INDEX "IX_PlatformSettingRevisions_ChangedBy" ON "PlatformSettingRevisions" ("ChangedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126052414_AddPlatformSettingVersions') THEN
    ALTER TABLE public."PlatformSettingRevisions" ENABLE ROW LEVEL SECURITY;
    REVOKE ALL ON TABLE public."PlatformSettingRevisions" FROM PUBLIC;
    DO $private_table$
    DECLARE role_name text;
    BEGIN
        FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
            WHERE rolname IN ('anon', 'authenticated', 'service_role')
        LOOP
            EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'PlatformSettingRevisions', role_name);
        END LOOP;
    END;
    $private_table$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126052414_AddPlatformSettingVersions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261126052414_AddPlatformSettingVersions', '10.0.15');
    END IF;
END $EF$;
COMMIT;
