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

`pageSize` is limited to 100. Account disable requires a reason, increments the session version, and revokes every refresh token in the same database transaction. The affected user's existing access tokens fail on their next request. Administrators cannot disable themselves or change their own role through these endpoints.

The admin UI must never ask for or display password hashes, MFA secrets, recovery-code hashes, refresh tokens or Data Protection keys; none are present in these response contracts. Treat `409` as a concurrency/workflow error, `429` as rate limiting, and all other non-success responses as RFC 7807 `ProblemDetails`.

Apply the provider-specific `AddUserAdministrationState` migration during an authorized deployment before using account status operations. Do not run it against shared or production data without the separate deployment approval.
