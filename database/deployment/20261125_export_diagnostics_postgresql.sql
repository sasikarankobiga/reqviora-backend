START TRANSACTION;
CREATE TABLE "ExportJobDiagnostics" (
    "Id" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "ApprovedRevisionId" uuid NOT NULL,
    "ExportRecordId" uuid,
    "ExportType" character varying(50) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "DurationMilliseconds" bigint,
    "FailureCode" character varying(100),
    "FailureCategory" character varying(50),
    "IntegrityStatus" character varying(20) NOT NULL,
    CONSTRAINT "PK_ExportJobDiagnostics" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ExportJobDiagnostics_ExportRecords_ExportRecordId" FOREIGN KEY ("ExportRecordId") REFERENCES "ExportRecords" ("Id"),
    CONSTRAINT "FK_ExportJobDiagnostics_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id")
);

CREATE INDEX "IX_ExportJobDiagnostics_ExportRecordId" ON "ExportJobDiagnostics" ("ExportRecordId");

CREATE INDEX "IX_ExportJobDiagnostics_ProjectId_CreatedAt" ON "ExportJobDiagnostics" ("ProjectId", "CreatedAt");

CREATE INDEX "IX_ExportJobDiagnostics_Status_CreatedAt" ON "ExportJobDiagnostics" ("Status", "CreatedAt");

ALTER TABLE public."ExportJobDiagnostics" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public."ExportJobDiagnostics" FROM PUBLIC;
DO $private_table$
DECLARE role_name text;
BEGIN
    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
        WHERE rolname IN ('anon', 'authenticated', 'service_role')
    LOOP
        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'ExportJobDiagnostics', role_name);
    END LOOP;
END;
$private_table$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261125122202_AddExportJobDiagnostics', '10.0.15');

COMMIT;
