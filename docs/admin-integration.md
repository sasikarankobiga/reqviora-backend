# Administrator web integration

Administrator APIs require a valid JWT whose current database role is `Admin` or `Administrator`, or whose email is explicitly configured in `Security:AdminEmails`. Administrator sign-in remains subject to mandatory MFA. A role claim alone is not trusted: token validation reloads the user, role and session version from the database on every request.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/admin/dashboard` | User, project, workspace, AI queue, audit and failed-login totals |
| GET | `/api/admin/users` | Paginated users; filters: `page`, `pageSize`, `search`, `status` |
| PUT | `/api/admin/users/{userId}/status` | Disable or re-enable an account |
| PUT | `/api/admin/users/{userId}/role` | Assign an existing database role or `User` |
| GET | `/api/admin/projects` | Paginated projects; filters: `page`, `pageSize`, `search`, `ownerId` |
| GET | `/api/admin/projects/{projectId}` | Project ownership and workflow counts |
| GET | `/api/admin/audit-events` | Paginated request audit; filters: `userId`, `statusCode`, `path` |
| GET | `/api/admin/mcp/grants` | Metadata-only MCP grant directory; filters: `page`, `pageSize`, `search`, `state`, `projectId` |
| GET | `/api/admin/mcp/grants/{grantId}` | One grant's metadata; credentials and project contents are never returned |
| POST | `/api/admin/mcp/grants/{grantId}/revoke` | Emergency revocation; requires a 3–500 character reason |
| GET | `/api/admin/exports/jobs` | Paginated metadata-only export diagnostics with status, project and date filters |
| GET | `/api/admin/exports/jobs/{exportJobId}` | One synchronous export attempt's sanitized lifecycle metadata |
| GET | `/api/admin/exports/summary` | Success/failure, duration and integrity totals for an optional date range |
| GET | `/api/admin/validation/summary` | Validation attempts, outcomes, percentages, average duration and category counts |
| GET | `/api/admin/validation/statistics`, `/api/admin/validation/trends` | Daily UTC validation statistics for days with recorded attempts |
| GET | `/api/admin/validation/error-categories` | Schema, broken reference, unsupported component, navigation and other counts |
| GET | `/api/admin/ai-jobs`, `/api/admin/ai-jobs/{jobId}` | AI job metadata only |
| GET | `/api/admin/ai-jobs/summary`, `/api/admin/ai-jobs/statistics` | Persisted queue counts, ages, durations, retries and latest recorded failure categories |
| GET | `/api/admin/ai-jobs/health` | Queue lease health, this API instance's worker signal, and recent provider evidence |
| GET | `/api/admin/settings`, `/api/admin/settings/{key}` | Allowlisted settings and effective values |
| PUT | `/api/admin/settings/{key}` | Append a version with `value`, `expectedRevision`, and `reason` |
| GET | `/api/admin/settings/{key}/history` | Paginated version history |
| POST | `/api/admin/settings/{key}/rollback` | Append a version from `targetRevision`, with `expectedRevision` and `reason` |
| GET, POST | `/api/admin/incidents` | Search/filter incidents or create a metadata-only incident |
| GET | `/api/admin/incidents/{incidentId}`, `/api/admin/incidents/{incidentId}/history` | Incident metadata and immutable transition history |
| PUT | `/api/admin/incidents/{incidentId}/status`, `/api/admin/incidents/{incidentId}/assignment` | Concurrency-protected status and assignment changes |
| GET | `/api/admin/incidents/summary` | Counts by status, critical priority and assignment state |
| GET, PUT | `/api/admin/profile` | Current administrator profile and display-name-only update |
| GET | `/api/admin/profile/security`, `/api/admin/profile/login-history` | MFA/account state, security events and personal login history |
| GET | `/api/admin/profile/sessions` | Active personal sessions without tokens or token hashes |
| DELETE | `/api/admin/profile/sessions/{sessionId}` | Revoke one personal session immediately |
| POST | `/api/admin/profile/sessions/revoke-others` | Revoke every personal session except the current bound session |

`pageSize` is limited to 100. Account disable requires a reason, increments the session version, and revokes every refresh token in the same database transaction. The affected user's existing access tokens fail on their next request. Administrators cannot disable themselves or change their own role through these endpoints.

The admin UI must never ask for or display password hashes, MFA secrets, recovery-code hashes, refresh tokens or Data Protection keys; none are present in these response contracts. Treat `409` as a concurrency/workflow error, `429` as rate limiting, and all other non-success responses as RFC 7807 `ProblemDetails`.

MCP grant state is `active`, `expired`, or `revoked`. Emergency revocation is immediate, idempotent, and records the administrator, timestamp, reason, and a security audit event. Admin APIs expose identifiers and lifecycle metadata only; they never return the raw credential, its hash, Blueprint data, prompts, exports, or project requirements.

Export diagnostics model the existing synchronous generator accurately: attempts transition directly from `Processing` to `Succeeded` or `Failed`. Responses contain no archive bytes, manifests, storage paths, download URLs, Blueprint JSON, exception text, or stack traces. Failures use normalized categories and codes.

Validation reports use inclusive UTC dates (`from` and `to`, YYYY-MM-DD). The default window is 30 days and the maximum range is 366 days. Percentages are on a 0–100 scale. A navigation error may also count as a broken reference. Telemetry starts at deployment and covers explicit Blueprint and Builder validations. Approval reuses its Blueprint validation run. Draft saves and approval guard checks do not add separate validation attempts.

Queue listing accepts `page`, `pageSize`, `status`, `type`, `from`, and `to`. The current job type is `completion`. Statistics use job creation time. Duration measures first start to completion, including retry delay. Failure counts reflect the latest persisted error per job. Provider health uses observed job outcomes from the last 30 minutes and reports `unknown` when no evidence is available. Worker health applies to the responding API instance. Queue lease health uses shared database state.

The supported runtime setting is `document-upload-max-bytes`. Revision 0 uses the lower of the configured storage limit and the document contract maximum of 25,000,000 bytes. Document uploads exceeding the effective setting return HTTP 413. Updates and rollbacks require the current revision and append a history entry and an audit event. A stale revision returns HTTP 409. AI, MCP and secret configuration stay outside the settings API.

Incidents accept only category, priority and an optional correlation ID at creation. Priorities are `Low`, `Medium`, `High`, and `Critical`; statuses are `Open`, `InProgress`, `Resolved`, and `Closed`. Every assignment and status change requires the latest `expectedConcurrencyToken`. Resolution notes are required when resolving, are length-limited, and reject common credential markers. Incident APIs never include project content, documents, prompts, or credentials.

Newly issued access tokens are bound to their persisted refresh-session identifier. Deleting that session therefore rejects the corresponding access token on its next request without invalidating unrelated sessions. Tokens issued before this deployment remain governed by the existing global session-version validation until their normal expiry. Profile updates accept `displayName` only; unknown fields such as roles are rejected. Password, verified-email change, MFA enrollment/recovery, and logout continue to use the existing account-security and authentication endpoints.

Apply the provider-specific `AddUserAdministrationState`, `CompleteMcpGrantLifecycle`, `AddExportJobDiagnostics`, `AddValidationHealth`, `AddPlatformSettingVersions`, and `AddSupportIncidentManagement` migrations during an authorized deployment. Reviewable scripts are in `database/deployment`. Do not run them against shared or production data without separate deployment approval.
