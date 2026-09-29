# AgitpropScraper — Requirements

## 1. Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-001 | Scrape article content from multiple news sites using site-specific XPath parsers. |
| FR-002 | Extract Hungarian named entities (PER, LOC, ORG, MISC) in-process using the provisioned .NET ONNX model. |
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
| NFR-001 | Architecture: Modular monolith orchestrated by Aspire 13.5.4. |
| NFR-002 | Language / runtime: C# / .NET 10.0.0; no Python runtime in the scraper deployment. |
| NFR-003 | Database: PostgreSQL 17 (pgAdmin UI on port 5050). |
| NFR-004 | Message broker: RabbitMQ 3.1+ (mgmt UI on 15672, AMQP on 5672). |
| NFR-005 | Proxy providers: ProxyScrape and RedScrape; configurable fallback to direct HTTP. |
| NFR-006 | Browser automation: PuppeteerSharp with Chromium; PuppeteerExtraSharp + plugins optional. |
| NFR-007 | Resiliency: Polly `WaitAndRetry` for transient sink operations and `CircuitBreaker` on HTTP calls. |
| NFR-008 | Observability: OpenTelemetry OTLP exporter (traces + metrics → collector). |
| NFR-009 | Health checks: Aspire built-in health endpoints; the consumer validates required ONNX model assets during startup. |
| NFR-010 | Docker: Multi-stage builds; images published to GHCR (`fortyfei/agitprop`). |
| NFR-011 | CI/CD: GitHub Actions (`aspire-publish.yml`) using Aspire image publishing on push to `main`. |
| NFR-012 | Access control: Internal developers only (no public exposure). |
| NFR-013 | NLP model files are pinned and checksum-verified during provisioning; application startup requires no internet access. |

## 3. Development Constraints

- Documentation lives under `.docs/` only; all other `.md` files at repo root are moved or deleted.
- Mermaid diagrams for every pipeline and component interaction.
- All connection strings (`newsfeed`, `messaging`) required at runtime; startup throws if missing.
- EF Core migrations are applied by the Web API startup path when
  `ASPNETCORE_ENVIRONMENT=Development` or `ApplyMigrationsAtStartup=true`.
- The CLI uses System.CommandLine with three commands: `scrape-article`, `scrape-archive`, `retry`.