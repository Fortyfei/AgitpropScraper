# Agitprop.Scraper.Consumer

## Purpose

The **PostgreSQL queue worker** that processes scraping jobs from the
`newsfeed_job` PGMQ queue. This is the core orchestration layer that ties together the
Spider, parsers, in-process NLP library, and the Newsfeed sink.

## Responsibilities

1. **Message consumption** — claims `NewsfeedJobDescrpition` messages using
   PGMQ visibility timeouts, then acknowledges successful jobs.
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

Requires the PostgreSQL connection string; throws `InvalidOperationException` if missing:

- `ConnectionStrings:newsfeed` (PostgreSQL)

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Host setup: AddServiceDefaults, ConfigureInfrastructureWithBrowser(false), ConfigurePgmq, AddNewsfeedSink |
| `Extensions.cs` | `ConfigurePgmq` |
| `Consumers/NewsfeedJobConsumer.cs` | Scrape job processor |
| `Consumers/NewsfeedJobQueueWorker.cs` | PGMQ polling, retry, acknowledgement, and failed-queue handling |

## Queue Registration (Extensions.cs)

### ConfigurePgmq

- Registers the shared `IPgmqClient` using `ConnectionStrings:newsfeed`.
- Registers the worker and scoped job processor.
- Creates the PGMQ extension and `newsfeed_job` / `newsfeed_job_error` queues at startup.
- The worker polls in batches up to `ConcurrencyLimit`, starts with a 120-second visibility timeout,
  and extends the lease every 30 seconds while a job is being processed.

### Telemetry collection

- The shared service defaults collect the consumer ActivitySource and Meter,
  correlating queue processing with scraper work and exporting it through OTLP
  when configured.
- Consumer metrics report job counts, outcome, and duration with bounded
  job-type/handling tags, including a dedicated discovered-job publish failure
  counter and a skipped-job counter. They do not include URLs, exception
  messages, or other per-job values as metric dimensions.

## Observability

The consumer's trace follows message consumption through Spider processing,
entity recognition, and database persistence. Custom URL fields in consumer
logs/spans omit query strings and fragments. EF Core spans provide query
timings without SQL text or parameter values. Unsupported hosts, invalid URL
schemes, and article asset URLs are logged and acknowledged without crawling;
the same eligibility check filters discovered jobs before publication.
Propagated failures are measured and passed to the PGMQ retry/error handling.
The consumer span includes visibility renewals and message acknowledgement or
retry/error routing. Routine queue polling is excluded from OpenTelemetry traces
to avoid standalone database spans; polling failures are logged and recorded as
worker error spans.

## Configuration

- Queue and database: `ConnectionStrings:newsfeed` → PostgreSQL with PGMQ enabled
- NLP model directory: `NLP__ModelDirectory` (defaults to `ner-model` beside the binaries)
- The model provisioner downloads pinned assets on first use and verifies their
  SHA-256 hashes. A pre-provisioned model directory can be used for offline startup.

## Retry behavior

The worker retries classified transient transport and database failures after
1, 2, and 5 seconds (PGMQ delays have whole-second precision). Permanent and
unknown failures are moved to `newsfeed_job_error`; requested cancellations are
left unacknowledged so PGMQ makes them visible again after the visibility timeout.
The CLI `retry` command moves failed messages back to the main queue.
