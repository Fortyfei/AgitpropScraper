# Agitprop.CLI

## Purpose

A **System.CommandLine** console application providing operator tools for
manual scraping, archive crawling, and retry of failed messages.

## Commands

| Command | Description |
|---------|-------------|
| `scrape-article` | Scrape a single article and print to console. |
| `scrape-archive` | Scrape archive pages and list article URLs. |
| `retry` | Re-queue failed feed messages from the PGMQ error queue. |

The root command initializes structured JSON console logging and registers the
three commands with a shared logger factory.

## Key Files

### Commands/

| File | Purpose |
|------|---------|
| `ScrapeArticleCommand.cs` | Defines `scrape-article` command with `--url` and `--shorten` options. Handler calls `ScrapeCommandOrchestrator`. |
| `ScrapeArchiveCommand.cs` | Defines `scrape-archive` with `--date`, `--from`, `--to`, `--newsite`, `--feed-config`, `--connection`, `--verbosity`, and `--verbose` options. Handler resolves date range and sites, then publishes jobs. |
| `RetryCommand.cs` | Defines `retry` with `--connection`, `--failed-queue`, `--target-queue`, and `--max`. Re-publishes failed messages. |

### Services/

| File | Purpose |
|------|---------|
| `ScrapeCommandOrchestrator.cs` | Implements `IScrapeCommandOrchestrator` — orchestrates scraping and retry via `IArticleJobPublisher`. |
| `IArticleJobPublisher.cs` | Interface + implementations: `PgmqArticleJobPublisher` and `NoOpPublisher`. Methods: `PublishAsync`, `RetryFailedFeedsAsync`. |
| `ArchiveCommandInputResolver.cs` | Resolves raw CLI input (dates, sites, verbosity) into `ArchiveCommandResolvedInput`. |
| `IScrapeCommandOrchestrator.cs` | Interface definition |

## Usage

```bash
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- --help
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- scrape-article --url <URL> --shorten
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- scrape-archive --date 2024-01-01
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- retry --connection "Host=localhost;Port=5432;Database=newsfeed;Username=postgres;Password=..." --failed-queue newsfeed_job_error --target-queue newsfeed_job --max 10
```
