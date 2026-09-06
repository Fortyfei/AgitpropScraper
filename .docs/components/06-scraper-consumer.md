# Agitprop.Scraper.Consumer

## Purpose

The **MassTransit consumer service** that processes scraping jobs from the
RabbitMQ queue. This is the core orchestration layer that ties together the
Spider, parsers, NLP service, and the Newsfeed sink.

## Responsibilities

1. **Message consumption** — consumes `ScrapingJobDescription` messages from
   the RabbitMQ queue (via `NewsfeedJobConsumer` + `NewsfeedJobConsumerDefinition`).
2. **Job building** — uses `ScrapingJobFactory` to construct a `ScrapingJob`
   with the appropriate `ContentParsers`, `LinkParsers`, and `Paginator`.
3. **Crawling** — calls `ISpider.CrawlAsync()` to crawl the target URL.
4. **NLP enrichment** — the sink (`NewsfeedSink`) calls the NLP service via
   `NamedEntityRecognizer` to extract named entities (PER/LOC/ORG/MISC) from
   article text.
5. **Persistence** — saves entities + mention links to PostgreSQL via EF Core.
6. **Migration** — applies EF Core migrations at startup when
   `ASPNETCORE_ENVIRONMENT=Development` or `ApplyMigrationsAtStartup=true`.

## Startup Validation

Requires two connection strings; throws `InvalidOperationException` if missing:

- `ConnectionStrings:newsfeed` (PostgreSQL)
- `ConnectionStrings:messaging` (RabbitMQ AMQP)

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Host setup: AddServiceDefaults, ConfigureInfrastructureWithBrowser(false), ConfigureMassTransit, ConfigureTracing, ConfigureMetrics, AddNewsfeedSink |
| `Extensions.cs` | `ConfigureMassTransit`, `ConfigureTracing`, `ConfigureMetrics` |
| `InternalExtensions.cs` | `GetExceptionMessage` (truncates to 256 chars), `GetDomainFromUrl` |
| `Consumers/NewsfeedJobConsumer.cs` | The MassTransit consumer implementation |
| `Consumers/NewsfeedJobConsumerDefinition.cs` | Queue/concurrency configuration |

## DI Registration (Extensions.cs)

### ConfigureMassTransit

- `SetKebabCaseEndpointNameFormatter` for endpoint naming.
- `SetInMemorySagaRepositoryProvider`.
- Registers `NewsfeedJobConsumer` + `NewsfeedJobConsumerDefinition`.
- RabbitMQ host from connection string.
- Clear serialization + AddRawJsonSerializer; ConfigureEndpoints.

### ConfigureTracing / ConfigureMetrics

- OpenTelemetry source/meter: `"Agitprop.NewsfeedJobConsumer"`.

## Configuration

- MassTransit: `ConnectionStrings:messaging` → RabbitMQ AMQP
- Database: `ConnectionStrings:newsfeed` → PostgreSQL
- NLP service address: configured via service discovery in AppHost
