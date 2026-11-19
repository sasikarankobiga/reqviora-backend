# REQVIORA Backend

Clean Architecture foundation for the REQVIORA API, targeting .NET 10 and PostgreSQL.

## Projects

- `Reqviora.Domain` — entities and domain building blocks
- `Reqviora.Application` — use cases, DTOs, and interfaces
- `Reqviora.Infrastructure` — persistence and external service adapters
- `Reqviora.Api` — ASP.NET Core HTTP API

## Run locally

### Prerequisites

- .NET SDK 10.0.400 or newer 10.0 feature-band patch
- PostgreSQL 16+, or Docker Desktop with Docker Compose

### Option 1: Docker Compose

Create a local `.env` from `.env.example`, replace the example password, then run:

```bash
docker compose up --build
```

The API is available at `http://localhost:8080`. The `.env` file is ignored by Git.

### Option 2: .NET CLI

Keep credentials outside tracked configuration. From the repository root:

```bash
dotnet user-secrets init --project src/Reqviora.Api
dotnet user-secrets set "ConnectionStrings:ReqvioraDatabase" "Host=localhost;Port=5432;Database=reqviora;Username=postgres;Password=<your-password>" --project src/Reqviora.Api
dotnet user-secrets set "Jwt:Key" "<a-random-secret-with-at-least-32-characters>" --project src/Reqviora.Api
dotnet restore
dotnet tool restore
dotnet build
dotnet run --project src/Reqviora.Api
```

You can alternatively set `ConnectionStrings__ReqvioraDatabase` as an environment variable.

## Development endpoints

- Swagger UI: `GET /swagger`
- OpenAPI JSON: `GET /swagger/v1/swagger.json`
- Liveness: `GET /health/live`
- Database readiness: `GET /health/ready`

Swagger is enabled only in the Development environment. The readiness endpoint reports unhealthy until PostgreSQL is reachable.

## Configuration

| Key | Required | Purpose |
| --- | --- | --- |
| `ConnectionStrings:ReqvioraDatabase` | Yes | PostgreSQL connection string |
| `Cors:AllowedOrigins` | For browser clients | Explicit frontend origins |
| `Jwt:Issuer` | Day 2 | Access-token issuer |
| `Jwt:Audience` | Day 2 | Access-token audience |
| `Jwt:Key` | Yes | Signing key supplied through secrets/environment |
| `Jwt:AccessTokenMinutes` | No | Access-token lifetime; defaults to 15 minutes |
| `Jwt:RefreshTokenDays` | No | Refresh-token lifetime; defaults to 7 days |

No passwords, connection credentials, or JWT signing keys should be committed.

## Build

```bash
dotnet restore
dotnet tool restore
dotnet build
dotnet test
```

## Database migrations

After configuring the connection string, apply the checked-in schema with:

```bash
dotnet tool restore
dotnet ef database update --project src/Reqviora.Infrastructure --startup-project src/Reqviora.Api
```

## Current implementation status

Day 1 provides API hosting, dependency injection, EF Core/PostgreSQL persistence registration, Swagger, CORS, health checks, centralized error responses, and safe configuration.

Day 2 provides password-hashed registration and login, signed JWT access tokens, hashed/rotating refresh-token sessions, logout/revocation, the authenticated `GET /api/users/me` endpoint, and owner-scoped protection on the existing project endpoints. Full Project Get/Update behavior remains part of Day 3.

Day 3 completes authenticated project creation, owner-scoped listing, single-project retrieval, and metadata updates. Project ownership always comes from the validated JWT claim; request bodies cannot assign or change the owner. Requests for another user's project return `404` without revealing whether that project exists.

### Authentication endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/users/me` (Bearer token required)

Refresh tokens are returned only when issued or rotated. The database stores their SHA-256 hashes, not the raw token values. Logout is idempotent and revokes an active refresh token.

### Project endpoints

- `POST /api/projects` - create a project for the authenticated user
- `GET /api/projects` - list only the authenticated user's projects
- `GET /api/projects/{id}` - read an owned project
- `PUT /api/projects/{id}` - update an owned project's name and description

All project endpoints require `Authorization: Bearer <access-token>`. A ready-to-run request collection is available at `docs/api/projects.http`.

### Requirement review

`POST /api/projects/{projectId}/requirements/{requirementId}/review` accepts a
Bearer token from the project owner. Send the decision and its expected
revision in an `application/json` request body.

```json
{"expectedRevision": 1, "decision": "accept"}
```

Supported decisions are `accept`, `edit`, `reject`, and `defer`. An `edit`
requires `editedTitle` and `editedDescription` and returns the requirement to
`suggested` until it is explicitly accepted. The API records the reviewer from
the authenticated token and returns the requirement ID, status, and new
revision. A stale revision or invalid status transition returns HTTP 409.

The review endpoint uses the existing `ConnectionStrings:ReqvioraDatabase`.
In development, apply the EF migrations above and the review schema in
`database/reqviora_review/001_create_review_store.sql` to that database before
using the route. The review script expects the `anon` and `authenticated`
roles provided by Supabase. For a standalone development PostgreSQL instance,
create those roles as `NOLOGIN` roles before running the script.

## Kobiga backend assignment

| Tasks | Implementation and verification |
| --- | --- |
| 1–3 | Blueprint v1 validation covers all six ID types, duplicate IDs, and broken references. [Schema](contracts/schemas/blueprint/blueprint.v1.schema.json) · [Semantic validator](src/Reqviora.Application/Features/UIGeneration/Blueprint/Validation/BlueprintContractValidator.cs) · [Schema tests](tests/Reqviora.Blueprint.SchemaTests/Program.cs) · [Graph tests](tests/Reqviora.Blueprint.ContractTests/Program.cs) |
| 4–7 | Input Studio covers text, documents, voice, and manual input. The input pipeline suite covers provider contracts, document parsing, source evidence mapping, and editable Tamil and English transcripts. [AI provider contract](src/Reqviora.Application/Features/AIOrchestration/ProviderContracts.cs) |
| 8–9 | Structured extraction limits candidates to known source evidence. Human review supports accept, edit, reject, defer, and clarification. The review API records decisions with revision checks and audit history. |
| 10–11 | The draft generator builds feature and screen graphs from confirmed requirements using the component registry. Regeneration validates provider output against Blueprint v1 and the graph validator, preserves confirmations and protected edits, and returns the current draft on invalid output or provider failure. |

The provider, document parser, and transcriber are application interfaces. Tests
supply fixture output while the G0-07 provider decision is pending. Blueprint
schema, semantic validation, input pipeline, generation, and review checks run
through the .NET 10 build and contract suites.
