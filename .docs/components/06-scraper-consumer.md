# Agitprop.Scraper.Consumer

## Purpose

The **MassTransit consumer service** that processes scraping jobs from the
RabbitMQ queue. This is the core orchestration layer that ties together the
Spider, parsers, in-process NLP library, and the Newsfeed sink.

## Responsibilities

1. **Message consumption** — consumes `ScrapingJobDescription` messages from
   the RabbitMQ queue (via `NewsfeedJobConsumer` + `NewsfeedJobConsumerDefinition`).
2. **Job building** — `INewsfeedPipeline` resolves the site's `INewsfeedSource`
   by host and builds the `ScrapingJob` (parsers, link parsers, paginator).
3. **Crawling** — the pipeline calls `ISpider.CrawlAsync()` and returns discovered jobs.
4. **NLP enrichment** — the sink (`NewsfeedSink`) calls
   `NamedEntityRecognizer` in-process to extract Hungarian named entities
   (PER/LOC/ORG/MISC) from article text using ONNX Runtime.
5. **Persistence** — saves entities + mention links to PostgreSQL via EF Core.
6. **Database ownership** — uses the shared PostgreSQL database; migrations are
   applied by the Web API startup path rather than by this worker.

## Startup Validation

Requires two connection strings; throws `InvalidOperationException` if missing:

- `ConnectionStrings:newsfeed` (PostgreSQL)
- `ConnectionStrings:messaging` (RabbitMQ AMQP)

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Host setup: AddServiceDefaults, ConfigureInfrastructureWithBrowser(false), ConfigureMassTransit, AddNewsfeedSink |
| `Extensions.cs` | `ConfigureMassTransit` |
| `Consumers/NewsfeedJobConsumer.cs` | The MassTransit consumer implementation |
| `Consumers/NewsfeedJobConsumerDefinition.cs` | Queue/concurrency configuration |

## DI Registration (Extensions.cs)

### ConfigureMassTransit

- `SetKebabCaseEndpointNameFormatter` for endpoint naming.
- `SetInMemorySagaRepositoryProvider`.
- Registers `NewsfeedJobConsumer` + `NewsfeedJobConsumerDefinition`.
- RabbitMQ host from connection string.
- Clear serialization + AddRawJsonSerializer; ConfigureEndpoints.

### Telemetry collection

- The shared service defaults collect the consumer and MassTransit ActivitySource
  and Meter, correlating broker spans with the scraper work and exporting them
  through OTLP when configured.
- Consumer metrics report job counts, outcome, and duration with bounded
  job-type/handling tags, including a dedicated discovered-job publish failure
  counter. They do not include URLs, exception messages, or other per-job
  values as metric dimensions.

## Observability

The consumer's trace follows message consumption through Spider processing,
entity recognition, and database persistence. Custom URL fields in consumer
logs/spans omit query strings and fragments. EF Core spans provide query
timings without SQL text or parameter values. Invalid jobs and propagated
failures are both measured; non-cancellation failures are propagated for
MassTransit retry and error-queue handling.

## Configuration

- MassTransit: `ConnectionStrings:messaging` → RabbitMQ AMQP
- Database: `ConnectionStrings:newsfeed` → PostgreSQL
- NLP model directory: `NLP__ModelDirectory` (defaults to `ner-model` beside the binaries)
- The model provisioner downloads pinned assets on first use and verifies their
  SHA-256 hashes. A pre-provisioned model directory can be used for offline startup.

## Retry behavior

The endpoint retries only classified transient failures, after 500 ms, 2 seconds,
and 5 seconds. Permanent and unknown failures, as well as requested
cancellations, are not retried. After all retries are exhausted, MassTransit
moves the failed message to its error queue for the CLI `retry` command to
requeue.
