# REQVIORA frontend API guide

All project-content endpoints require `Authorization: Bearer <access-token>`. Owners have full access, Editors may read and edit workflow content, and Viewers have read-only access. Approval, export creation, metadata updates, and member administration remain owner-only. JSON errors use ASP.NET `ProblemDetails`. A missing or invalid token returns `401`; unauthorized project access returns `403`; missing content returns `404`; and stale draft tokens return `409`.

## Authentication and projects

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| POST | `/api/auth/register` | `{ "email", "password", "displayName" }` | `201 AuthResponseDto` |
| POST | `/api/auth/login` | `{ "email", "password" }` | `200 AuthResponseDto` |
| POST | `/api/auth/refresh` | `{ "refreshToken" }` | `200 AuthResponseDto` |
| POST | `/api/auth/logout` | `{ "refreshToken" }` | `204` |
| POST | `/api/account-security/email-verification/request` | none; Bearer required | `202` or `204` if already verified |
| POST | `/api/account-security/email-verification/confirm` | `{ "token" }` | `204` |
| POST | `/api/account-security/password/forgot` | `{ "email" }` | `202` for known and unknown accounts |
| POST | `/api/account-security/password/reset` | `{ "token", "newPassword" }` | `204` |
| POST | `/api/account-security/email-change/request` | `{ "newEmail" }`; Bearer required | `202` |
| POST | `/api/account-security/email-change/confirm` | `{ "token" }` | `204` |
| GET | `/api/users/me` | none | `200 UserProfileDto` |
| GET/POST | `/api/projects` | `CreateProjectDto` for POST | project list / `201` |
| GET/PUT | `/api/projects/{projectId}` | `UpdateProjectDto` for PUT | project / `200` |
| GET | `/api/projects/{projectId}/members` | none | owner/member roster |
| PUT | `/api/projects/{projectId}/members/{userId}` | `{ "role": "Viewer" | "Editor" }` | update an accepted member |
| DELETE | `/api/projects/{projectId}/members/{userId}` | none | `204` |
| POST/GET | `/api/projects/{projectId}/invitations` | create body: `{ "recipientEmail", "role" }` | `201` / owner-only list |
| POST | `/api/projects/{projectId}/invitations/{invitationId}/revoke` | none | revoke a pending invitation |
| POST | `/api/projects/{projectId}/invitations/{invitationId}/resend` | none | replace token and resend email |
| POST | `/api/project-invitations/accept` | `{ "token" }` | atomically create membership |
| POST | `/api/project-invitations/reject` | `{ "token" }` | reject without membership |
| GET | `/api/projects/{projectId}/overview` | none | project workflow counts and current token |
| GET | `/api/workspaces` | none | owned workspaces |
| GET | `/api/workspaces/{workspaceId}` | none | owned workspace |

Registration passwords must contain 8–128 characters.

New members must use the verified-email invitation lifecycle; the member `PUT` route no longer grants first-time access. See [secure project invitations](project-invitations.md) for response fields, errors, expiry, token handling, and Flutter examples.

Project create and update use the same nine frontend fields: `name`, `description`, `industry`, `targetAudience`, `projectType`, `primaryPlatform`, `technologyPreferences`, `constraints`, and `integrations`. The owner identity is always taken from the access token and is never writable from the request body.

## Draft and Blueprint workflow

Create the first draft without a token. Every successful write returns a new `concurrencyToken`; send that value as `expectedConcurrencyToken` on the next write.

```json
{
  "draft": { "projectNotes": "Draft content" },
  "expectedConcurrencyToken": null
}
```

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/projects/{projectId}/drafts/current` | Resume the current draft |
| PUT | `/api/projects/{projectId}/drafts/current` | Save generic draft JSON |
| GET | `/api/projects/{projectId}/drafts/versions` | Immutable version history |
| POST | `/api/projects/{projectId}/drafts/versions/{version}/restore` | Restore a historical snapshot as a new current version |
| POST | `/api/projects/{projectId}/drafts/start-after-approval` | Start a child draft from the approved revision |
| GET | `/api/projects/{projectId}/blueprints/current` | Current Blueprint v1 document |
| PUT | `/api/projects/{projectId}/blueprints/current` | Save draft JSON and a Blueprint v1 document |
| GET | `/api/projects/{projectId}/blueprints/versions` | Blueprint version history |
| POST | `/api/projects/{projectId}/blueprints/validate` | Validate the current version using its draft token |
| GET | `/api/projects/{projectId}/blueprints/validations/{validationId}` | Validation result and findings |
| GET | `/api/projects/{projectId}/blueprints/approved` | Approved immutable Blueprints |
| GET | `/api/projects/{projectId}/blueprints/approved/{revisionId}` | One approved Blueprint |

A stale or missing token on an existing draft returns `409`. Approval locks that draft. Call `start-after-approval` with its latest token before making further changes.

## Eight-step guided builder

The typed builder is persisted inside the same versioned draft under `guidedBuilder` (`schemaVersion: "1.0"`). Send the latest draft `concurrencyToken` as `expectedConcurrencyToken` on every step after the first. This preserves generic draft compatibility while preventing essential builder data from becoming opaque JSON.

| Method | Route | Typed step |
| --- | --- | --- |
| GET | `/api/projects/{projectId}/builder` | Resume all steps and the current token |
| PUT | `/api/projects/{projectId}/builder/steps/identity-audience` | Identity, audience, personas, roles and sources |
| PUT | `/api/projects/{projectId}/builder/steps/features-screens` | Requirements, features, screens and coverage mappings |
| PUT | `/api/projects/{projectId}/builder/steps/navigation` | Initial screen, routes, outcomes, conditions and roles |
| PUT | `/api/projects/{projectId}/builder/steps/theme-brand` | Palette, tokens, typography, spacing, borders, elevation and breakpoints |
| PUT | `/api/projects/{projectId}/builder/steps/components-layout` | Registry, properties, hierarchy and responsive layouts |
| PUT | `/api/projects/{projectId}/builder/steps/interactions-states` | Fields, validation, actions, UI states and transitions |
| PUT | `/api/projects/{projectId}/builder/steps/ai-design-workspace` | Protection, editable properties, alternatives, autosave and history |
| PUT | `/api/projects/{projectId}/builder/steps/review-preview` | Preview graph, review decisions and coverage readiness |
| POST | `/api/projects/{projectId}/builder/validate` | Complete cross-step validation report |

Each PUT body is `{ "data": <typed-step>, "expectedConcurrencyToken": <token-or-null> }`. Writes reject broken IDs, unsupported component variants/properties, hierarchy cycles, unknown design tokens and invalid state/navigation references. Completion validation also reports missing steps, uncovered requirements, unused or unreachable screens, and incomplete review coverage.

## Requirements and generated graph

These endpoints return only persisted Blueprint content; they do not invoke an AI provider or fabricate missing data.

| Method | Route |
| --- | --- |
| GET | `/api/projects/{projectId}/requirements` |
| GET | `/api/projects/{projectId}/requirements/{requirementId}` |
| POST | `/api/projects/{projectId}/requirements/{requirementId}/review` |
| GET | `/api/projects/{projectId}/features` |
| GET | `/api/projects/{projectId}/features/{featureId}` |
| GET | `/api/projects/{projectId}/screens` |
| GET | `/api/projects/{projectId}/screens/{screenId}` |
| GET | `/api/projects/{projectId}/navigation` |

Review decisions are `accept`, `edit`, `reject`, or `defer` and require `expectedRevision`. `edit` also requires `editedTitle` and `editedDescription`.

## Approval and Second Brain export

```json
{
  "expectedConcurrencyToken": "current-draft-token",
  "validationRunId": "passing-validation-id"
}
```

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/projects/{projectId}/approvals` | Approve the current validated version |
| GET | `/api/projects/{projectId}/approvals` | Approval history |
| GET | `/api/projects/{projectId}/approvals/{revisionId}` | Approval status |
| POST | `/api/projects/{projectId}/exports/from-revision/{revisionId}` | Create or reuse a verified ZIP |
| GET | `/api/projects/{projectId}/exports` | Export metadata |
| GET | `/api/projects/{projectId}/exports/{exportId}` | One export's metadata |
| GET | `/api/projects/{projectId}/exports/{exportId}/download` | Authorized ZIP download |

An approval requires a passing validation run for the exact current draft version. When a `guidedBuilder` is present, all eight steps and their cross-step coverage must also pass before approval. Export generator 1.1 packages 24 manifest-tracked artifacts plus `manifest.json`, including the SRS, requirement traceability matrix, developer handoff, typed design documentation, `universal-blueprint.json`, `asset-manifest.json`, and immutable `project.json`. Export reads verify every file digest, the manifest, and the archive SHA-256 before returning content.

## Billing and PayHere checkout

Billing is disabled by default. The client must never send or calculate the payable amount. Checkout returns a provider-hosted PayHere form action and fields already signed by the backend. A browser return or cancel redirect is informational only; poll the project billing status until the backend has processed a verified PayHere notification.

| Method | Route | Authorization | Purpose |
| --- | --- | --- | --- |
| GET | `/api/billing/pricing?projectId={projectId}` | Project reader when a project is supplied | Current server-side USD price and gateway availability |
| POST | `/api/projects/{projectId}/billing/checkout` | Project owner | Create a pending order and provider-hosted checkout fields |
| GET | `/api/projects/{projectId}/billing/status` | Project reader | Latest payment and entitlement state |
| GET | `/api/projects/{projectId}/billing/history?page=1&pageSize=50` | Project reader | Paginated order history |
| GET | `/api/billing/payments/{paymentId}` | Related project reader or administrator | One payment without cardholder data |
| POST | `/api/billing/payhere/notify` | Anonymous provider callback; signed form data required | Idempotently verify and apply a PayHere event |
| GET | `/api/admin/billing/payments` | Administrator | Filtered, paginated payment monitoring |
| GET | `/api/admin/billing/payments/{paymentId}` | Administrator | Payment detail |
| GET | `/api/admin/billing/summary` | Administrator | Persisted lifecycle counts and successful USD total |
| PUT | `/api/admin/billing/projects/{projectId}/pricing` | Administrator | Replace the active project USD price using `{ "amountMinor": 12500 }` |
| POST | `/api/admin/billing/payments/{paymentId}/refund` | Administrator | Request a provider refund using `{ "reason": "..." }` |

Checkout contact fields are `firstName`, `lastName`, `email`, `phone`, `address`, `city`, and `country`. The response contains `paymentId`, `orderId`, `actionUrl`, `fields`, and `expiresAt`; submit `fields` directly to `actionUrl`. Status values are `Pending`, `Processing`, `Succeeded`, `Failed`, `Cancelled`, `Refunded`, or `Disputed`. Entitlement is granted only after a signed success notification and is revoked on a verified chargeback or completed refund. The API never receives or stores card numbers or CVV values.

The frontend develop branch inspected on 26 November 2026 does not yet call these billing routes. It also still calls legacy `/api/projects/{projectId}/workflow-intelligence/*` paths. Current backend workflow routes are the Requirements, Clarifications, Blueprints, Drafts, Guided Builder, and Approval routes documented above; frontend migration or a separately approved compatibility adapter is required before browser E2E can pass.

## Configuration

- `ConnectionStrings__ReqvioraDatabase`
- `Jwt__Key` (at least 32 characters)
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__AccessTokenMinutes`
- `Jwt__RefreshTokenDays`
- `AccountLinks__BaseUrl` (HTTPS frontend origin for account-action links)
- `Cors__AllowedOrigins__0` and subsequent indexes for browser clients

Swagger is available at `/swagger` in Development. Apply the EF migrations and the matching PostgreSQL `.sql` or SQL Server `.sqlserver.sql` Requirement Review schema before using all endpoints.

Administrator Web uses the role-protected `/api/admin` surface documented in `admin-integration.md`. Account disable and role changes revoke existing sessions. These routes are not available to ordinary project owners, editors or viewers.

Administrator profile screens use `/api/admin/profile`, `/security`, `/sessions`, and `/login-history`. A profile update may send only `displayName`. Session responses contain IDs and lifecycle timestamps only; they never contain access tokens, refresh tokens, hashes, password data, MFA secrets, or recovery codes. After deleting a session, treat a `401` from that session as final and return it to sign-in. Password change, email change, MFA and logout remain on the existing `/api/account-security` and `/api/auth` routes.

## Input intelligence and feature-gated MCP V1 scope

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/projects/{projectId}/input-intelligence/requirements/extract` | Generate evidence-bound requirement candidates; body must include `consentGranted: true` and human review is still mandatory |
| POST | `/api/projects/{projectId}/input-intelligence/voice/transcribe` | Transcribe consented Tamil or English multipart audio |
| GET | `/api/mcp/v1/catalog` | Feature-gated read-only tools, resources and prompt catalog |
| POST | `/api/mcp/v1/tools/call` | Invoke an allow-listed owner-scoped read-only tool |
| GET | `/api/mcp/v1/prompts/{name}` | Read an approved V1 prompt template |
| POST | `/api/projects/{projectId}/mcp/grants` | Owner creates a grant bound to an approved revision; raw credential is returned once |
| GET | `/api/projects/{projectId}/mcp/grants` | Owner lists metadata for their project's grants |
| POST | `/api/projects/{projectId}/mcp/grants/{grantId}/revoke` | Owner revokes a project grant with a reason |

Text extraction and voice transcription validate explicit consent and project authorization before checking provider availability. Input-intelligence and AI-job endpoints return `503` while `AI__Enabled=false`. Keep this default until G0-07 approves a provider, then configure the approved endpoint, models, and secret-backed key before enabling it. Set `Mcp__Enabled=true` only after a separate MCP scope approval. The V1 surface contains no mutating MCP tool. Tool calls also require the one-time-issued credential in `X-MCP-Grant`; the grant must match the authenticated owner and requested project and must remain unexpired and unrevoked. Store that credential in an approved client secret store because it cannot be retrieved again.
## Private document downloads

`GET /api/projects/{projectId}/documents/{documentId}/download` always requires an authenticated project reader. With local storage it streams the file through the backend. With the Supabase private-storage provider it returns `{ "url": "https://...", "expiresAt": "..." }`; the URL is short-lived and must not be persisted or shared. Storage object paths and the backend service key are never returned to Flutter or Admin Web clients.
