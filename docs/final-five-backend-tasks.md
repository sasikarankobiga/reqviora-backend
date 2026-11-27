# Final five backend tasks

## Verified implementation on `feature/final-five-backend-tasks`

- **BE-01:** When `AI:Enabled` is true, Blueprint generation calls the structured provider and accepts only a parsed, schema-valid, project-bound Blueprint whose requirement records exactly match the confirmed review snapshots. Provider failures do not write a draft. When AI is explicitly disabled, the deterministic planner remains available and is identified by `X-Reqviora-Blueprint-Generation: deterministic`.
- **BE-02:** MCP credentials resolve to their grant-bound `ApprovedRevisionId`; tools read immutable approved snapshots rather than mutable drafts. Expired, revoked, missing, cross-project, and missing-revision grants are rejected.
- **BE-03:** The general administrator project directory now returns only operational metadata. It excludes project descriptions and owner email addresses, as well as existing draft/snapshot/export/document content.
- **BE-04:** Authenticated users can list redacted sessions, revoke an owned session, and revoke other sessions. Access-token validation already checks the bound refresh-session state, so revoking one session leaves unrelated sessions active.
- **BE-05:** The shared `Administrator` policy now requires both an administrator role and current MFA enrollment. The claim is rebuilt from database state during token validation, so disabled, revoked, or stale sessions cannot retain administrator access.

## Verification

Local verification uses .NET SDK 10.0.401 isolated at `Z:\REQVIORA_UT011611\toolcache\dotnet`, with NuGet and temporary artifacts on Z:. Focused MCP/session/privacy tests passed 28/28, administrator policy tests passed 38/38, and Blueprint generation safety tests passed 2/2. Live AI verification requires separately authorized provider credentials.

No frontend source, production/shared database, branch history, or remote branch was modified.
