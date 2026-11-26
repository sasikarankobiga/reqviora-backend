# Flutter integration handoff

Use the Development OpenAPI document at `/swagger/v1/swagger.json` to generate or verify Dart DTOs. Production should not expose Swagger UI; keep the checked frontend models aligned with the documented contracts and CI OpenAPI test.

## Authentication and errors

- Send `Authorization: Bearer <accessToken>` on project APIs.
- Refresh through `POST /api/auth/refresh`; never store the refresh token in application logs.
- Treat `401` as unauthenticated, `403` as authenticated but outside the project role, `404` as absent content, `409` as a stale concurrency token, and `429` as retryable only after bounded backoff.
- Decode non-success JSON as RFC 7807 `ProblemDetails`. Display `title` to the user and retain the `X-Correlation-ID` response header for support diagnostics.

## Draft and builder state

Call `GET /api/projects/{projectId}/builder` when opening or resuming the guided builder. Persist the returned `concurrencyToken` in page state. Every step PUT uses:

```json
{
  "data": {},
  "expectedConcurrencyToken": "latest-token"
}
```

Replace the local token only after a successful response. On `409`, reload the builder and let the user reconcile local edits. Do not automatically overwrite a newer server version. The eight step routes and typed fields are listed in `frontend-api.md`; `POST /api/projects/{projectId}/builder/validate` supplies completion findings before Blueprint approval.

## Documents, jobs and exports

- Upload documents as multipart data, then invoke the document parse endpoint. A `requires_ocr` state is retryable.
- Poll durable AI job state by job ID; do not assume an in-process request survives a backend restart.
- A local document download is a streamed response. A Supabase-backed download returns a short-lived signed URL; open it immediately and never persist it.
- Export downloads are immutable approved ZIPs. Validate the filename/content type client-side, while the backend validates the manifest and SHA-256 before serving.

## Health and release checks

`/health/live` confirms the process is running. `/health/ready` includes database, review schema, SMTP or antivirus dependencies configured for the deployment and may correctly return an unhealthy status during dependency outages. Flutter should not use health endpoints as an authenticated session check.
