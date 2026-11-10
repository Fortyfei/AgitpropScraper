# Agitprop.AppHost

## Purpose

The main **Aspire distributed application host** that orchestrates the entire
AgitpropScraper system. This is the entry point for `dotnet run --project
Agitprop.AppHost/Agitprop.AppHost.csproj`.

## Responsibilities

- Creates the `DistributedApplicationBuilder`.
- Registers the container registry (`ghcr.io/fortyfei/agitprop`) with
  `WithEnvironmentAwareImagePush`.
- Defines the Docker Compose environment with the Aspire dashboard on port `18888`.
- Provisions all infrastructure resources:
  - **PostgreSQL 17 + PGMQ** (`postgres`): `ghcr.io/pgmq/pg17-pgmq:v1.13.1`,
    data volume, pgAdmin on `5050`, persistent lifetime, OTLP exporter; attaches database `newsfeed`.
- Adds all .NET projects to the application model:
  - `consumer` — waits for and references `newsfeedDb`; runs Hungarian NER in-process.
  - `rss-feed-reader` — waits for and references `newsfeedDb`, and waits for `consumer`.
  - `backend` (Web.Api) — waits for and references `newsfeedDb`; it does not access the queue.
  - `frontend` (Web.Client) — waits for `backend`, external HTTP endpoints.
- AppHost-managed services use OTLP exporters. The containerized projects are
  configured for environment-aware image publishing.

## Key File

- `Agitprop.AppHost/AppHost.cs` — main orchestration definition.
- `Agitprop.AppHost/Extensions.cs` — optional extensions.

The repository-level Aspire configuration points to this canonical AppHost.
