# Secure project invitations

Project membership is granted only when the verified recipient accepts an invitation. All routes require a Bearer access token and use `application/json`. Invitation operations have a per-user rate limit of five requests per minute; excess requests return `429`.

## Endpoints

| Method | Route | Authorized actor | Request | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/api/projects/{projectId}/invitations` | Project owner | `{ "recipientEmail": "person@example.com", "role": "Viewer" }` | `201 ProjectInvitationDetails` |
| `GET` | `/api/projects/{projectId}/invitations` | Project owner | none | `200 ProjectInvitationDetails[]` |
| `POST` | `/api/projects/{projectId}/invitations/{invitationId}/revoke` | Project owner | none | `200 ProjectInvitationDetails` |
| `POST` | `/api/projects/{projectId}/invitations/{invitationId}/resend` | Project owner | none | `200 ProjectInvitationDetails` |
| `POST` | `/api/project-invitations/accept` | Verified invited recipient | `{ "token": "<token-from-email>" }` | `200 AcceptedProjectMembership` |
| `POST` | `/api/project-invitations/reject` | Verified invited recipient | `{ "token": "<token-from-email>" }` | `200 ProjectInvitationDetails` |

Only `Viewer` and `Editor` are valid invitation roles. Recipient email comparison is normalized and case-insensitive. The raw token is delivered by email only: list and mutation responses never contain the raw token or its hash. The database stores a SHA-256 hash.

`ProjectInvitationDetails` contains `id`, `projectId`, `recipientEmail`, `role`, `status`, `createdBy`, `createdAt`, `expiresAt`, actor/time pairs for acceptance, rejection and revocation, and `supersedesInvitationId`. Status is one of `Pending`, `Accepted`, `Rejected`, `Revoked`, or `Expired`.

The existing member routes remain available. `PUT /api/projects/{projectId}/members/{userId}` now updates the role of an existing accepted member only. It returns `409` when the user is not already a member, so it cannot bypass invitation acceptance. `DELETE` still removes an existing member.

## Errors and lifecycle rules

- `400`: invalid email, token shape, or role.
- `401`: absent or invalid access token.
- `403`: non-owner management, token used by a different email account, or cross-project access.
- `404`: project or invitation identifier does not exist.
- `409`: duplicate pending invitation, unverified recipient, accepted/rejected/revoked/expired token, replay, already-existing membership, or concurrent state change.
- `429`: invitation rate limit exceeded.
- `503`: invitation state was stored but email delivery failed. The owner may resend after email is restored; resend invalidates the previous token.

Acceptance updates the invitation and creates the membership in one database transaction. The unique pending-recipient key, unique token hash, unique project membership, and concurrency token protect multi-instance deployments. Resend revokes the prior record and issues a new random token. Rejection and revocation are final.

Expiration is configured with `Invitations:ExpirationHours` (default `72`, allowed range `1`–`720`). PostgreSQL migration access is backend-private: RLS is enabled and privileges are revoked from `PUBLIC`, `anon`, `authenticated`, and `service_role`. The backend database owner/role applies and accesses the table; do not expose it through the Supabase Data API.

## Flutter example

```dart
final create = await dio.post(
  '/api/projects/$projectId/invitations',
  data: {'recipientEmail': email.trim(), 'role': 'Viewer'},
);

// The deep-link handler obtains the email token and sends it only in the body.
final accepted = await dio.post(
  '/api/project-invitations/accept',
  data: {'token': invitationToken},
);
```

Treat the emailed token as sensitive: do not log it, persist it in analytics, or place it in subsequent URLs. Remove it from navigation state after accept/reject finishes.
