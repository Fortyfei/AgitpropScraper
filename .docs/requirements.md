# AgitpropScraper — Requirements

## 1. Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-001 | Scrape article content from multiple news sites using site-specific XPath parsers. |
| FR-002 | Extract named entities (PER, LOC, ORG, MISC) from article text using a spaCy-based NLP service. |
| FR-003 | Persist scraped articles, entities, and mention links to PostgreSQL (`newsfeed` database). |
| FR-004 | Publish scraped jobs to RabbitMQ for downstream processing. |
| FR-005 | Re-queue failed feed messages via the CLI (`dotnet agitprop retry …`). |
| FR-006 | Serve entity browse and analytics data via a read-only REST API (GET endpoints only). |
| FR-007 | Export structured entity & article data for dashboard/AI downstream consumption. |
| FR-008 | Generate trending-entity lists per date range from the mention store. |
| FR-009 | Pull RSS feeds on a configurable interval and publish jobs to the queue. |
| FR-010 | Support HTTP proxy rotation for resilient web crawling (optional mode). |
| FR-011 | Capture OpenTelemetry traces and metrics across all components. |
| FR-012 | Enable graceful startup with proxy pool initialization and cookie storage. |

## 2. Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | Architecture: Modular monolith orchestrated by Aspire 13.4.2. |
| NFR-002 | Language / runtime: C# 12 / .NET 10.0.0; Python 3.12 for NLP service. |
| NFR-003 | Database: PostgreSQL 16 (pgAdmin UI on port 5050). |
| NFR-004 | Message broker: RabbitMQ 3.1+ (mgmt UI on 15672, AMQP on 5672). |
| NFR-005 | Proxy providers: ProxyScrape and RedScrape; configurable fallback to direct HTTP. |
| NFR-006 | Browser automation: PuppeteerSharp with Chromium; PuppeteerExtraSharp + plugins optional. |
| NFR-007 | Resiliency: Polly `WaitAndRetry` (NLP service: 3 retries) and `CircuitBreaker` on HTTP calls. |
| NFR-008 | Observability: OpenTelemetry OTLP exporter (traces + metrics → collector). |
| NFR-009 | Health checks: Aspire built-in health endpoints + custom `/health` on NLP service. |
| NFR-010 | Docker: Multi-stage builds; images published to GHCR (`fortyfei/agitprop`). |
| NFR-011 | CI/CD: GitHub Actions (`aspire-publish.yml` / `docker-bake.hcl`) on push to `main`. |
| NFR-012 | Access control: Internal developers only (no public exposure). |

## 3. Development Constraints

- Documentation lives under `.docs/` only; all other `.md` files at repo root are moved or deleted.
- Mermaid diagrams for every pipeline and component interaction.
- All connection strings (`newsfeed`, `messaging`) required at runtime; startup throws if missing.
- EF Core migrations applied at startup when `ASPNETCORE_ENVIRONMENT=Development` or
  `ApplyMigrationsAtStartup=true`.
- The CLI uses System.CommandLine with three commands: `scrape-article`, `scrape-archive`, `retry`.