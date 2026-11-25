# Durable AI jobs

REQVIORA stores AI jobs in the application database. Jobs survive process restarts and are safe for multiple API instances because a worker must atomically acquire a time-limited database lease before processing a row.

## API

All endpoints require an authenticated user and are disabled while `AI:Enabled` is false.

- `POST /api/ai/jobs` creates a job. The JSON body contains `projectId` and `prompt`; the `Idempotency-Key` request header is required. Repeating the owner/project/key tuple returns the original job.
- `GET /api/ai/jobs/{id}` returns only a job created by the current user.
- `POST /api/ai/jobs/{id}/cancel` persistently requests cancellation. Pending jobs become cancelled immediately; a running worker observes the request and cancels the provider token.

Project owners and editors may create jobs for projects they can write. Unrelated users receive the standard authorization response before a job is stored.

## State machine

`pending -> running -> completed`

Provider failures and timeouts move a leased job back to `pending` with bounded exponential backoff until `MaxAttempts` is exhausted, after which it becomes `failed`. A pending or running job can become `cancelled`. An expired `running` lease is eligible for another worker after a restart; an expired final-attempt lease is safely finalized as failed.

Provider exception details are logged server-side. Persisted/API failure messages are sanitized and never contain credentials or raw provider responses.

## Configuration

The `AIJobs` section supports:

- `PollIntervalMilliseconds` (100–30000)
- `LeaseSeconds` (30–3600; the worker automatically extends this beyond the job timeout)
- `MaxAttempts` (1–10)
- `BaseRetrySeconds` and `MaxRetrySeconds`
- `MaxPromptCharacters` (1–1000000)
- `MaxResultCharacters` (1–10000000)

The AI provider timeout and credential remain in the `AI` section. Credentials must come from deployment secrets and must not be written to appsettings files.

## Database deployment

- PostgreSQL migration: `20261125085005_AddPersistentAiJobs`
- SQL Server migration: `20261125085009_AddPersistentAiJobs`
- Review-only idempotent scripts: `database/deployment/20261125_postgresql_pending.sql` and `database/deployment/20261125_sqlserver_pending.sql`

The PostgreSQL migration enables RLS and removes table access from `PUBLIC` and any existing Supabase client roles. The backend database identity still needs the intended private table privileges. Do not apply either script to a shared or production database without human approval and a disposable-database migration test.
