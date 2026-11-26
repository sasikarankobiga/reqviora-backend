# Core workflow database deployment checks

## Before deployment

- Use a disposable local PostgreSQL/SQL Server or isolated development database first.
- Keep credentials in environment variables or user secrets; never commit connection strings.
- Confirm the project/database identity and obtain team approval before changing a shared Supabase database.
- Take a backup and verify an isolated restore before applying schema changes.
- Run the existing provider migration tests in `ProjectDraftServiceTests` (including `HasPendingModelChanges` and native provider tests). The Task 07B concurrency-token metadata changes require model/snapshot agreement; they do not introduce new columns.

## Schema deployment order

1. Apply the checked-in EF Core migrations for the intended provider using the commands in `README.md` and `rashmina-persistence.md`.
2. Apply `database/reqviora_review/001_create_review_store.sql` and `002_create_clarifications.sql` in order for PostgreSQL. For SQL Server use their `.sqlserver.sql` counterparts, with a client that handles `GO` batches.
3. Keep the review schema private, and verify the backend database role can access it. Do not grant browser roles access or expose it through the Supabase Data API.
4. Start the API and check `/health/live` and `/health/ready`. The review readiness probe verifies that the three tables and their expected columns are readable; it does not apply migrations, check all constraints or prove write permissions. HTTP 503 means a readiness dependency is unavailable. Inspect server-side logs without sharing credentials.
5. On a disposable local database, run both review smoke executables with their respective `REQVIORA_REVIEW_PG` and `REQVIORA_REVIEW_SQLSERVER` variables. They execute DDL and write fixtures; never run them against shared Supabase (including through a localhost tunnel).
6. Verify a real authorized workflow in the intended development environment: create project, insert/review requirement, answer clarification, generate/save/regenerate/approve blueprint, reload and export. Check persistence after API restart. Do not substitute mock data for deployment acceptance.

## CI

Backend CI runs on the core workflow feature branch and can be manually dispatched. It uses disposable PostgreSQL and SQL Server services, runs the native database suites and review smoke executables, and verifies PostgreSQL backup/restore schema presence. These tests do not prove connectivity or behavior in the team's actual Supabase environment.
