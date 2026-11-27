START TRANSACTION;
CREATE TABLE "ValidationTelemetry" (
    "Id" uuid NOT NULL,
    "CompletedAtMilliseconds" bigint NOT NULL,
    "UtcDay" integer NOT NULL,
    "Workflow" character varying(16) NOT NULL,
    "Passed" boolean NOT NULL,
    "DurationMilliseconds" bigint NOT NULL,
    "SchemaFailures" integer NOT NULL,
    "BrokenReferences" integer NOT NULL,
    "UnsupportedComponents" integer NOT NULL,
    "NavigationFailures" integer NOT NULL,
    "OtherFailures" integer NOT NULL,
    CONSTRAINT "PK_ValidationTelemetry" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_ValidationTelemetry_UtcDay" ON "ValidationTelemetry" ("UtcDay");

ALTER TABLE public."ValidationTelemetry" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public."ValidationTelemetry" FROM PUBLIC;
DO $private_table$
DECLARE role_name text;
BEGIN
    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
        WHERE rolname IN ('anon', 'authenticated', 'service_role')
    LOOP
        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'ValidationTelemetry', role_name);
    END LOOP;
END;
$private_table$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126051526_AddValidationHealth', '10.0.15');

COMMIT;
