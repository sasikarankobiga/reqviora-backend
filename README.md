# Reqviora Backend

Clean Architecture starter for the Reqviora API, targeting .NET 8.

## Projects

- `Reqviora.Domain` — entities and domain building blocks
- `Reqviora.Application` — use cases, DTOs, and interfaces
- `Reqviora.Infrastructure` — persistence and external service adapters
- `Reqviora.Api` — ASP.NET Core HTTP API

## Run locally

```bash
dotnet restore
dotnet build
dotnet run --project src/Reqviora.Api
```

The starter health endpoint is available at `GET /health`.
