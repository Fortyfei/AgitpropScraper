# Agitprop.Scraper.NLPService

## Purpose

A **Python FastAPI microservice** that performs **Named Entity Recognition** (NER)
on article text using the **spaCy** Hungarian language model `hu_core_news_lg`.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check |
| POST | `/analyzeSingle` | Analyze a single text → returns `List<NamedEntity>` |
| POST | `/analyzeBatch` | Analyze multiple texts → returns `List<NamedEntityCollection>` |
| GET | `/discovery` | Service discovery info: lists all endpoints |

## Named Entity Types

The service extracts entities classified as:

- **PER** — Person names
- **LOC** — Locations
- **ORG** — Organizations
- **MISC** — Miscellaneous

These are aggregated via `NamedEntityCollection` (PER / LOC / ORG / MISC / All).

## Technology Stack

- **Framework**: FastAPI
- **NLP**: spaCy (`hu_core_news_lg` model)
- **Server**: uvicorn (on `PORT` env, default `8111`)
- **Observability**: OpenTelemetry metrics (histogram for
  `http.server.request.duration` via OTLP gRPC)
- **Runtime**: Python 3.12+

## C# Client

`Agitprop.Scraper.NLPService/NamedEntityRecognizer.cs`:

- Implements `INamedEntityRecognizer`.
- Uses `HttpClient` with **Polly** `WaitAndRetry` (3 retries configured via
  `Retry:NLPService=3`).
- POSTs to `/analyzeSingle` → deserializes `List<NamedEntity>` → wraps in
  `NamedEntityCollection`.
- `PingAsync()` checks `GET /health`.

## Deployment

Started by `Agitprop.AppHost` as:

```csharp
builder.AddUvicornApp("nlpservice", "../Agitprop.Scraper.NLPService", "app:app")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter();
```

## Local Development

```bash
cd Agitprop.Scraper.NLPService
pip install -r requirements.txt
python -m spacy download hu_core_news_lg
uvicorn app:app --port 8111
```
