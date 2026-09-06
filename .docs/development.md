# AgitpropScraper — Development

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0.0+ |
| Python | 3.12+ |
| Docker Desktop | Latest |
| Git | Latest |
| VS Code / Rider | Latest |

## 1. Clone & Restore

```bash
git clone https://github.com/<org>/AgitpropScraper.git
cd AgitpropScraper
dotnet restore
```

## 2. Local Development

### 2.1 Start Full Infrastructure (Aspire)

```bash
# Starts all services, dashboard on http://localhost:18888
dotnet run --project Agitprop.AppHost/Agitprop.AppHost.csproj
```

**What starts:**
- RabbitMQ (mgmt 15672, AMQP 5672)
- PostgreSQL (pgAdmin 5050)
- NLP Service (FastAPI on :8111)
- Consumer (runs spider + MassTransit)
- RSS Feed Reader
- Web API (Swagger on :5000)
- Web Client (Blazor on :5001)

### 2.2 Run Individual Services

| Service | Command |
|---------|---------|
| Web API only | `dotnet run --project Agitprop.Web.Api/Agitprop.Web.Api.csproj` |
| Web Client only | `dotnet run --project Agitprop.Web.Client/Agitprop.Web.Client.csproj` |
| Consumer only | `dotnet run --project Agitprop.Scraper.Consumer/Agitprop.Scraper.Consumer.csproj` |
| RSS Feed Reader | `dotnet run --project Agitprop.Scraper.RssFeedReader/Agitprop.Scraper.RssFeedReader.csproj` |
| CLI | `dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- --help` |
| NLP Service | `cd Agitprop.Scraper.NLPService && pip install -r requirements.txt && uvicorn app:app --port 8111` |

### 2.3 Environment Variables / Connection Strings

Required in `appsettings.Development.json` or user-secrets:

```json
{
  "ConnectionStrings": {
    "newsfeed": "Host=localhost;Port=5432;Database=newsfeed;Username=postgres;Password=postgres",
    "messaging": "amqp://guest:guest@localhost:5672"
  },
  "Proxy": {
    "UseProxies": false,
    "TargetAliveProxies": 25,
    "MinAliveProxies": 10,
    "StartupTimeoutMinutes": 5,
    "WaitTimeoutSeconds": 30
  }
}
```

## 3. Build & Test

### 3.1 Build All

```bash
dotnet build Agitprop.slnx
```

### 3.2 Run Unit Tests

```bash
# All tests
dotnet test

# Content parser offline tests (fixtures)
dotnet test Agitprop.Sinks.Newsfeed_Test/Agitprop.Sinks.Newsfeed_Test.csproj \
  --filter "FullyQualifiedName~ContentParserOfflineTests"

# Content parser online tests (live sites)
dotnet test Agitprop.Sinks.Newsfeed_Test/Agitprop.Sinks.Newsfeed_Test.csproj \
  --filter "FullyQualifiedName~ContentParserOnlineTests"
```

### 3.3 Run Content Parser Maintenance

```bash
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"
```

## 4. Database Migrations

EF Core migrations are applied automatically at startup when:
- `ASPNETCORE_ENVIRONMENT=Development`, OR
- `ApplyMigrationsAtStartup=true`

To create a new migration:

```bash
dotnet ef migrations add <Name> \
  --project Agitprop.Sinks.Newsfeed/Agitprop.Sinks.Newsfeed.csproj \
  --startup-project Agitprop.Scraper.Consumer/Agitprop.Scraper.Consumer.csproj
```

## 5. CLI Commands

```bash
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- --help
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- scrape-article --url <URL> --shorten
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- scrape-archive --date 2024-01-01
dotnet run --project Agitprop.CLI/Agitprop.CLI.csproj -- retry --failedQueue <queue> --targetQueue <queue> --max 10
```

## 6. Adding a New Content Parser

1. Create a new class in `Agitprop.Sinks.Newsfeed/Scrapers/ContentParsers/` extending `BaseArticleContentParser`.
2. Implement abstract properties:
   - `DateXPaths`, `TitleXPaths`, `LeadXPaths`, `ArticleXPaths` (list of XPath strings)
   - `SourceSite` (enum value from `NewsSites`)
3. Add case in `ContentParserFactory.GetContentParser(NewsSites site)`.
4. Add HTML fixtures under `Agitprop.Sinks.Newsfeed_Test/ContentParserTests/Offline/<Site>/` with expected output.
5. Run offline tests: `dotnet test --filter "FullyQualifiedName~ContentParserOfflineTests"`.

## 7. Useful VS Code Tasks

| Task | Description |
|------|-------------|
| `build` | `dotnet build` with full paths |
| `Run Full Infrastructure` | Start Aspire AppHost |
| `Run Web App` | Start Web API + Client only |
| `Run Worker` | Start Worker variant |
| `Run Content Parser Online Tests` | Filtered online tests |
| `Run Content Parser Offline Tests` | Filtered offline tests |

## 8. Port Reference

| Service | Port |
|---------|------|
| Aspire Dashboard | 18888 |
| RabbitMQ Management | 15672 |
| RabbitMQ AMQP | 5672 |
| pgAdmin | 5050 |
| PostgreSQL | 5432 |
| NLP Service | 8111 |
| Web API | 5000 (Swagger on /swagger) |
| Web Client | 5001 |
| Web API (Aspire) | Dynamic (check dashboard) |

## 9. Troubleshooting

| Issue | Fix |
|-------|-----|
| `InvalidOperationException: Connection string 'newsfeed' not found` | Add connection string to `appsettings.Development.json` or user-secrets. |
| `MSB3491` / `CS2012` file lock on build | Run `aspire stop` then `aspire start`; or delete `bin/obj` folders. |
| Port already in use | Aspire uses randomized ports in isolated mode; check dashboard for actual ports. |
| NLP service fails to load spaCy model | `cd Agitprop.Scraper.NLPService && python -m spacy download hu_core_news_lg` |
| Proxy initialization timeout | Set `Proxy.StartupTimeoutMinutes` higher, or disable proxies (`UseProxies: false`). |
| Blazor client not loading | Ensure `Microsoft.AspNetCore.Components.WebAssembly.Server` package is referenced. |