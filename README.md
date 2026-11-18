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
| `Jwt:Key` | Day 2 | Signing key supplied through secrets/environment |

No passwords, connection credentials, or JWT signing keys should be committed.

## Build

```bash
dotnet restore
dotnet tool restore
dotnet build
```

## Database migrations

After configuring the connection string, apply the checked-in schema with:

```bash
dotnet tool restore
dotnet ef database update --project src/Reqviora.Infrastructure --startup-project src/Reqviora.Api
```

## Current implementation status

Day 1 provides API hosting, dependency injection, EF Core/PostgreSQL persistence registration, Swagger, CORS, health checks, centralized error responses, and safe configuration. Authentication and project business workflows remain intentionally incomplete for the next assignment days.
