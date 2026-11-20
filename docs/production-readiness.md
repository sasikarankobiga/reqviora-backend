# Production readiness

## Required secrets

Supply `ConnectionStrings__ReqvioraDatabase`, `Jwt__Key`, and SMTP credentials through the deployment platform's secret store. Do not place production values in `appsettings.json` or `.env` committed to Git. Set `Database__Provider=SqlServer` when using the checked-in SQL Server migrations; PostgreSQL is the default.

Keep `AI__Enabled=false` while G0-07 provider approval is pending. After explicit approval, supply `AI__ApiKey` from the secret store, set the approved `AI__Endpoint`, `AI__Model`, and `AI__TranscriptionModel`, and then set `AI__Enabled=true`. Keep `Mcp__Enabled=false` unless its read-only scope is separately approved; the implementation exposes no mutating MCP tools.

Set `Security__AdminEmails__0` (and subsequent indexes) for administrator accounts. Each administrator must call `POST /api/security/mfa/provision`, store the one-time recovery codes offline, and confirm the authenticator with `POST /api/security/mfa/verify`. Administrators and suspicious sign-ins require TOTP after enrollment.

## Database and backup

Apply migrations as a release step before shifting traffic. Back up PostgreSQL with encrypted `pg_dump`/managed snapshots or SQL Server with encrypted full, differential, and transaction-log backups. Keep backups in a separate account/region, enable point-in-time recovery, and test a restore quarterly. The readiness probe is `/health/ready`.

## Documents and exports

Mount `Storage__RootPath` on private durable storage. Public web serving must not expose this directory. Production must set `Storage__RequireAntivirus=true` and connect the ClamAV daemon. Uploads are size-limited, receive random storage names, and are scanned before persistence. Approved exports are verified with their manifest and SHA-256 before being recorded and copied into private artifact storage.

## Monitoring and retention

Forward structured application logs and security alerts to the production monitoring platform. Alert on repeated login failures, MFA challenges, 5xx rates, readiness failures, AI-provider errors, SMTP failures, antivirus failures, and backup failures. API audit events default to 2,555 days and login telemetry to at most 365 days; adjust `Security__AuditRetentionDays` to match policy and applicable law.

## Release verification

Run `dotnet test Reqviora.sln --configuration Release`, the contract suites and both Requirement Review store smoke tests in `.github/workflows/backend-ci.yml`, migration smoke tests against both database engines, an SMTP delivery test, an EICAR antivirus rejection test, structured extraction, Tamil and English transcription, MFA recovery, an approved-revision export integrity check, and a restore drill before declaring the release production-ready.
