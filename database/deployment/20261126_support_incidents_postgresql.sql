START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE TABLE "SupportIncidents" (
        "Id" uuid NOT NULL,
        "Reference" character varying(30) NOT NULL,
        "Category" character varying(80) NOT NULL,
        "Priority" character varying(20) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "ReporterId" uuid NOT NULL,
        "AssignedAdminId" uuid,
        "CorrelationId" character varying(100),
        "ResolutionNotes" character varying(1000),
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedAtUnixTimeMilliseconds" bigint NOT NULL,
        "ResolvedAt" timestamp with time zone,
        "ClosedAt" timestamp with time zone,
        "ConcurrencyToken" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "LastModifiedAt" timestamp with time zone,
        "LastModifiedBy" text,
        CONSTRAINT "PK_SupportIncidents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SupportIncidents_Users_AssignedAdminId" FOREIGN KEY ("AssignedAdminId") REFERENCES "Users" ("Id"),
        CONSTRAINT "FK_SupportIncidents_Users_ReporterId" FOREIGN KEY ("ReporterId") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE TABLE "SupportIncidentHistory" (
        "Id" uuid NOT NULL,
        "IncidentId" uuid NOT NULL,
        "Action" character varying(40) NOT NULL,
        "PreviousValue" character varying(100),
        "NewValue" character varying(100),
        "Notes" character varying(1000),
        "ChangedBy" uuid NOT NULL,
        "ChangedAt" timestamp with time zone NOT NULL,
        "ChangedAtUnixTimeMilliseconds" bigint NOT NULL,
        CONSTRAINT "PK_SupportIncidentHistory" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SupportIncidentHistory_SupportIncidents_IncidentId" FOREIGN KEY ("IncidentId") REFERENCES "SupportIncidents" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SupportIncidentHistory_Users_ChangedBy" FOREIGN KEY ("ChangedBy") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE INDEX "IX_SupportIncidentHistory_ChangedBy" ON "SupportIncidentHistory" ("ChangedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE INDEX "IX_SupportIncidentHistory_IncidentId_ChangedAtUnixTimeMillisec~" ON "SupportIncidentHistory" ("IncidentId", "ChangedAtUnixTimeMilliseconds");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE INDEX "IX_SupportIncidents_AssignedAdminId" ON "SupportIncidents" ("AssignedAdminId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE UNIQUE INDEX "IX_SupportIncidents_Reference" ON "SupportIncidents" ("Reference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE INDEX "IX_SupportIncidents_ReporterId" ON "SupportIncidents" ("ReporterId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    CREATE INDEX "IX_SupportIncidents_Status_Priority_UpdatedAtUnixTimeMilliseco~" ON "SupportIncidents" ("Status", "Priority", "UpdatedAtUnixTimeMilliseconds");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    ALTER TABLE public."SupportIncidents" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE public."SupportIncidentHistory" ENABLE ROW LEVEL SECURITY;
    REVOKE ALL ON TABLE public."SupportIncidents", public."SupportIncidentHistory" FROM PUBLIC;
    DO $private_tables$
    DECLARE role_name text;
    BEGIN
        FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
            WHERE rolname IN ('anon', 'authenticated', 'service_role')
        LOOP
            EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'SupportIncidents', role_name);
            EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'SupportIncidentHistory', role_name);
        END LOOP;
    END;
    $private_tables$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261126053530_AddSupportIncidentManagement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261126053530_AddSupportIncidentManagement', '10.0.15');
    END IF;
END $EF$;
COMMIT;
