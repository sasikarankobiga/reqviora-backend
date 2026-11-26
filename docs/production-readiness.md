# Production readiness

## Required secrets

Supply `ConnectionStrings__ReqvioraDatabase`, `Jwt__Key`, and SMTP credentials through the deployment platform's secret store. Do not place production values in `appsettings.json` or `.env` committed to Git. Set `Database__Provider=SqlServer` when using the checked-in SQL Server migrations; PostgreSQL is the default. Set `AccountLinks__BaseUrl` to the authorized HTTPS frontend origin.

Production startup fails closed unless HTTPS frontend/CORS origins, TLS SMTP credentials, an absolute private storage path, and required ClamAV settings are present. `/health/ready` checks the database plus configured SMTP and antivirus TCP endpoints so the deployment platform does not route traffic while a required dependency is unavailable.

Keep `AI__Enabled=false` while G0-07 provider approval is pending. After explicit approval, supply `AI__ApiKey` from the secret store, set the approved `AI__Endpoint`, `AI__Model`, and `AI__TranscriptionModel`, and then set `AI__Enabled=true`. Keep `Mcp__Enabled=false` unless its read-only scope is separately approved; the implementation exposes no mutating MCP tools.

AI and transcription HTTP adapters use `AI__MaxAttempts` (1–5), `AI__BaseRetryMilliseconds` (10–5000), `AI__TimeoutSeconds` (10–600), and `AI__MaxResponseCharacters` (1000–4000000). Only timeouts, network failures, HTTP 408/429 and 5xx responses are retried. Provider response bodies, prompts, audio and credentials are excluded from surfaced protocol exceptions.

Set `Security__AdminEmails__0` (and subsequent indexes) for administrator accounts. Each administrator must use an authenticated bootstrap session to call `POST /api/security/mfa/provision`, store the one-time recovery codes offline, and confirm the authenticator with `POST /api/security/mfa/verify`. Configured administrators cannot complete a later password login until MFA is enrolled; after enrollment every administrator login requires TOTP or a single-use recovery code.

## Database and backup

Apply migrations as a release step before shifting traffic. Back up PostgreSQL with encrypted `pg_dump`/managed snapshots or SQL Server with encrypted full, differential, and transaction-log backups. Keep backups in a separate account/region, enable point-in-time recovery, and test a restore quarterly. The readiness probe is `/health/ready`.

For Supabase, use the direct connection or an appropriate Supavisor mode for the workload and require verified TLS in the Npgsql connection string. Install the project CA certificate and use bounded pooling/timeouts, for example `SSL Mode=VerifyFull;Root Certificate=/run/secrets/supabase-ca.crt;Pooling=true;Timeout=15;Command Timeout=30`. The application owns authorization through ASP.NET Core JWTs and a server database connection; it does not map application users to Supabase `auth.uid()`. EF tables therefore remain backend-private: PostgreSQL migrations enable RLS as defense in depth and revoke table access from `PUBLIC`, `anon`, `authenticated`, and `service_role`. Do not add broad Data API policies.

SMTP credentials must come from the deployment secret provider. Configure `Smtp:TimeoutSeconds`, `Smtp:MaxAttempts`, and `Smtp:BaseRetryMilliseconds` within the production validation bounds. Only transient SMTP failures and provider timeouts are retried; permanent failures and caller cancellation are not. Verification tokens and OTP bodies must never be added to exception messages or application logs.

Before release, confirm the migration list against the authorized Supabase project, run security/performance/health advisors, verify managed-backup retention and PITR entitlement, and perform a restore into an isolated project. Never test a restore over the live database.

CI runs `.github/scripts/postgres-backup-restore-drill.sh`: it creates a custom-format dump, verifies its SHA-256 checksum, restores into a randomly named isolated database, verifies restored tables, and removes only that drill database. Production restore drills must use the same isolation rule and approved backup credentials.

## Documents and exports

Mount `Storage__RootPath` on private durable storage. Public web serving must not expose this directory. Production must set `Storage__RequireAntivirus=true` and connect the ClamAV daemon. Uploads are size-limited, receive random storage names, and are scanned before persistence. Approved exports are verified with their manifest and SHA-256 before being recorded and copied into private artifact storage.

For Supabase Storage, set `Storage__Provider=Supabase`, an HTTPS `Storage__SupabaseUrl`, the pre-created private bucket name, and a secret-backed service key. The application never creates or changes buckets. Keep `Storage__SignedUrlSeconds` between 30 and 900 seconds. The service key is backend-only; clients receive only project-authorized, short-lived download URLs.

## Containers

The Docker image uses a multi-stage Release publish, runs as the official ASP.NET image's non-root `APP_UID`, and exposes liveness through its image health check. `.dockerignore` excludes Git metadata, build output, test artifacts, local environment files, certificates, keys, dumps and private artifacts.

Copy `.env.example` to an untracked local `.env` only for isolated development. Production deployments must inject the same variables through the orchestrator's secret/configuration facilities. Do not bake `.env`, certificates or credentials into the image.

Linux production containers require a password-protected Data Protection certificate mounted read-only at `/run/secrets/data_protection_certificate`. Mount `/var/lib/reqviora/data-protection-keys` and `/var/lib/reqviora/private-artifacts` on separate durable private volumes. Losing the key volume or certificate prevents protected MFA and OTP payloads from surviving replacement containers.

Validate configuration without starting services using `docker compose config --quiet`. Start an isolated development stack with `docker compose up --build`; readiness remains unhealthy until PostgreSQL, the private review schema, SMTP and ClamAV are reachable. Apply migrations as a separate controlled release step—application container startup does not mutate the schema automatically.

## Monitoring and retention

Forward structured application logs and security alerts to the production monitoring platform. Alert on repeated login failures, MFA challenges, 5xx rates, readiness failures, AI-provider errors, SMTP failures, antivirus failures, and backup failures. API audit events default to 2,555 days and login telemetry to at most 365 days; adjust `Security__AuditRetentionDays` to match policy and applicable law.

## Release verification

Run `dotnet test Reqviora.sln --configuration Release`, the contract suites and both Requirement Review store smoke tests in `.github/workflows/backend-ci.yml`, migration smoke tests against both database engines, an SMTP delivery test, an EICAR antivirus rejection test, structured extraction, Tamil and English transcription, MFA recovery, an approved-revision export integrity check, and a restore drill before declaring the release production-ready.
