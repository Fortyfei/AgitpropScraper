# Agitprop.Scraper.RssFeedReader

## Purpose

A **.NET worker hosted service** that periodically reads the configured RSS
feeds and publishes the articles found in them as `NewsfeedJobDescrpition`
messages to the **PGMQ queue in PostgreSQL** for downstream scraping by the
Consumer.

The reader does not scrape articles and does not check the database for already
collected ones — that is the Consumer's `CheckPageAlreadyVisited` check. What it
owns is feed reading plus a bounded in-memory watermark, so a feed that did not
change does not become queue traffic on every cycle.

The job contract is defined in the shared Core project, so the RSS reader does
not depend on the Newsfeed sink project.

## Responsibilities

1. Every `IntervalMinutes` (default 60; the first cycle runs immediately at
   startup) reads all configured feeds with **conditional GET**
   (`If-None-Match` / `If-Modified-Since`). A `304 Not Modified` read is a no-op.
2. Parses items with `System.ServiceModel.Syndication` and normalizes each link:
   relative links are resolved against the feed URL, the query string and
   fragment are dropped, and only http(s) links are kept. An item without a
   usable link is counted and skipped instead of failing the whole feed.
3. Publishes only links that were **not published before**, as one transactional
   batch to `newsfeed_job` via `IPgmqClient.SendBatchAsync`.
4. Advances the watermark and the HTTP validators **only after a successful
   publish**, so a failed cycle is retried instead of silently losing articles.

Feeds are read with bounded concurrency (`MaxConcurrentFeeds`, default 4) and
fail in isolation: one broken feed makes the cycle a `partial_failure` instead
of blocking the other feeds.

## Dedupe state

`RssFeedState` is an in-memory singleton. Per feed it keeps the
ETag/Last-Modified pair and a FIFO set of the last `CapacityPerFeed` (2000)
published links, stored as 64-bit hashes. The memory a feed can take is
therefore fixed (tens of KB) and does not grow with uptime.

A restart loses the state, so the first cycle after a deploy republishes the
current feed contents once; the Consumer's database check drops those
duplicates. There is intentionally no periodic full re-publish.

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Host setup: `AddServiceDefaults()`, options, named `HttpClient("rss")` with a user agent, `IRssFeedClient`, `AddPgmqClient("newsfeed")` |
| `RssFeedReader.cs` | `BackgroundService`: scheduling (`TimeProvider` + `PeriodicTimer`), bounded parallel fetch, dedupe, publish, all telemetry |
| `RssFeedClient.cs` | Conditional GET, byte-limited download, DTD-safe parse, link normalization |
| `RssFeedState.cs` | In-memory watermark and HTTP validators per feed |
| `RssFeedReaderOptions.cs` | Configuration loading and fail-fast validation |

## Configuration

In `appsettings.json`:

```json
{
  "Feeds": [
    "https://example.com/feed1.xml",
    "https://example.com/feed2.xml"
  ],
  "IntervalMinutes": 60,
  "FeedRequestTimeoutSeconds": 30,
  "MaxConcurrentFeeds": 4,
  "MaxFeedBytes": 5242880,
  "MaxItemsPerFeed": 500
}
```

`Feeds` is required and must contain absolute http(s) URLs. An empty list,
duplicate entries, invalid URLs or a non-positive numeric option fail startup
with every problem listed in one message.

## Observability

- ActivitySource and Meter: `"Agitprop.RssFeedReader"`.
- One `rss.cycle` root per cycle; the hosted service keeps no span open for its
  lifetime, and `BoundedTraceSampler` always keeps the cycle root.

| Span | Bounded attributes |
|------|--------------------|
| `rss.cycle` | `rss.feeds.configured`, `rss.feeds.not_modified`, `rss.feeds.failed`, `rss.items.parsed`, `rss.items.invalid`, `rss.items.duplicate`, `rss.items.published`, `outcome` |
| `rss.feed` | `feed.host`, `http.response.status_code`, `feed.not_modified`, `feed.items.count`, `feed.items.invalid`, `feed.truncated`, `failure.reason`, `outcome` |
| `rss.publish` | `messaging.destination.name`, `messaging.batch.size` |

- `outcome` is `success`, `partial_failure` (some feeds failed), `failure`
  (every feed failed or the cycle threw) or `cancelled`.
- `PgmqClient` opens a separate `Producer` root span per message, so a batch is
  exported as one trace per job; `agitprop.batch.origin_trace_id` links them to
  the cycle. No span or event is created per feed item.
- Metrics: `rss.cycle.duration` (`outcome`), `rss.feed.duration` (`feed.host`,
  `outcome`), `rss.items.fetched` (`feed.host`), `rss.items.duplicate`
  (`feed.host`), `rss.jobs.published`, `rss.feed.failures` (`feed.host`,
  `reason`: `http`, `timeout`, `invalid`, `parse`, `unknown`), `rss.publish.failures`.
- Logs: one Information summary line per cycle, Debug per feed, Warning for a
  failed feed, Error for a cycle-level failure.
- Feed URLs are logged without query string or fragment; spans only carry
  `feed.host`.

## Safety limits

| Limit | Purpose |
|-------|---------|
| `MaxFeedBytes` (5 MB) | the response stream is counted while parsing, so an oversized feed is rejected before it can balloon the heap |
| `MaxItemsPerFeed` (500) | bounds the batch one feed can contribute in a cycle; truncation is logged and flagged on the span |
| `MaxConcurrentFeeds` (4) | bounds how many feed documents are in memory at once |
| `FeedRequestTimeoutSeconds` (30) | a hanging feed cannot hold the cycle; the failure is reported per feed |

The parser runs with `DtdProcessing.Prohibit` and no `XmlResolver`, so a feed
cannot pull in external entities.

## Startup

Started by `Agitprop.AppHost` as project `rss-feed-reader`. It waits for
PostgreSQL and for the consumer to start.

Requires `ConnectionStrings:newsfeed` (PostgreSQL with PGMQ enabled).
