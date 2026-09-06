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
  - **NLP Service** (`nlpservice`): `uvicorn app:app` from
    `../Agitprop.Scraper.NLPService`, with `/health` check; OTLP.
- Adds all .NET projects to the application model:
  - `consumer` — waits for `newsfeedDb`, `messaging`, `nlpService`; references all.
  - `rssreader` — waits for `messaging`, `consumer`.
  - `backend` (Web.Api) — waits for `newsfeedDb`, `messaging`.
  - `frontend` (Web.Client) — waits for `backend`, external HTTP endpoints.
- Each project uses `WithOtlpExporter` and `WithEnvironmentAwareImagePush`.

## Key File

- `Agitprop.AppHost/AppHost.cs` — main orchestration definition.
- `Agitprop.AppHost/Extensions.cs` — optional extensions.

## Configuration

- `appsettings.Development.json` — development configuration overrides.
