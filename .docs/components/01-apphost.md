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
  - **RabbitMQ** (`messaging`): management plugin on `15672`, AMQP on `5672`
    (external), with OTLP exporter.
  - **PostgreSQL** (`postgres`): data volume, pgAdmin on `5050`, persistent
    lifetime, OTLP exporter; attaches database `newsfeed`.
- Adds all .NET projects to the application model:
  - `consumer` — waits for and references `newsfeedDb` and `messaging`; runs Hungarian NER in-process.
  - `rss-feed-reader` — waits for and references `messaging`, and waits for `consumer`.
  - `backend` (Web.Api) — waits for and references `newsfeedDb`; it has no RabbitMQ dependency.
  - `frontend` (Web.Client) — waits for `backend`, external HTTP endpoints.
- AppHost-managed services use OTLP exporters. The containerized projects are
  configured for environment-aware image publishing.

## Key File

- `Agitprop.AppHost/AppHost.cs` — main orchestration definition.
- `Agitprop.AppHost/Extensions.cs` — optional extensions.

The repository-level Aspire configuration points to this canonical AppHost.
