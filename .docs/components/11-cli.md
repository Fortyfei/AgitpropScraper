# Agitprop.CLI

## Purpose

A **System.CommandLine** console application providing operator tools for
manual scraping, archive crawling, and retry of failed messages.

## Commands

| Command | Description |
|---------|-------------|
| `scrape-article` | Scrape a single article and print to console. |
| `scrape-archive` | Scrape archive pages and list article URLs. |
| `retry` | Re-queue failed feed messages from RabbitMQ error queue. |

## Root Command

```csharp
var rootCommand = new RootCommand("Agitprop CLI Tool");
rootCommand.AddScrapeArticleCommand();
rootCommand.AddScrapeArchiveCommand();
rootCommand.AddRetryCommand();
await rootCommand.InvokeAsync(args);
```

## Key Files

### Commands/

| File | Purpose |
|------|---------|
| `ScrapeArticleCommand.cs` | Defines `scrape-article` command with `--url` and `--shorten` options. Handler calls `ScrapeCommandOrchestrator`. |
| `ScrapeArchiveCommand.cs` | Defines `scrape-archive` command with `--date`, `--from`, `--to`, `--newsites`, `--feedConfigPath`, `--connection`, `--verbose` options. Handler resolves date range and sites, then publishes jobs. |
| `RetryCommand.cs` | Defines `retry` command with `--connection`, `--failedQueue`, `--targetQueue`, `--max` options. Re-publishes failed messages. |

### Services/

| File | Purpose |
|------|---------|
| `ScrapeCommandOrchestrator.cs` | Implements `IScrapeCommandOrchestrator` — orchestrates scraping and retry via `IArticleJobPublisher`. |
| `IArticleJobPublisher.cs` | Interface + implementations: `RabbitMqPublisher` and `NoOpPublisher`. Methods: `PublishAsync`, `RetryFailedFeedsAsync`. |
| `ArchiveCommandInputResolver.cs` | Resolves raw CLI input (dates, sites, verbosity) into `ArchiveCommandResolvedInput`. |
| `IScrapeCommandOrchestrator.cs` | Interface definition |
| `IArticleJobPublisher.cs` | Interface for publishing jobs to RabbitMQ |

## Usage

```bash
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- --help
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- scrape-article --url <URL> --shorten
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- scrape-archive --date 2024-01-01
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- retry --failedQueue <queue> --targetQueue <queue> --max 10
```
