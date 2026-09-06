# AgitpropScraper — Deployment

## 1. Aspire Orchestration

The project uses **Aspire 13.4.2** for orchestration. The main `Agitprop.AppHost/AppHost.cs` configures:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var registry = builder.AddContainerRegistry("ghcr");

var env = builder.AddDockerComposeEnvironment("agitprop");
env.WithDashboard(port: 18888);

var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin(port: 15672)
    .WithEndpoint("amqp", port: 5672, isExternal: true)
    .WithOtlpExporter();

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin(port: 5050)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithOtlpExporter();

var newsfeedDb = postgres.AddDatabase("newsfeed");

var nlpService = builder.AddUvicornApp("nlpservice", "../Agitprop.Scraper.NLPService", "app:app")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter();

var consumer = builder.AddProject<Projects.Agitprop_Scraper_Consumer>("consumer")
    .WithReference(newsfeedDb).WaitFor(newsfeedDb)
    .WithReference(messaging).WaitFor(messaging)
    .WithReference(nlpService).WaitFor(nlpService);

var rssReader = builder.AddProject<Projects.Agitprop_Scraper_RssFeedReader>("rssreader")
    .WithReference(messaging).WaitFor(messaging)
    .WaitFor(consumer);

var backend = builder.AddProject<Projects.Agitprop_Web_Api>("backend")
    .WithReference(newsfeedDb).WaitFor(newsfeedDb)
    .WithReference(messaging).WaitFor(messaging);

var frontend = builder.AddProject<Projects.Agitprop_Web_Client>("frontend")
    .WithReference(backend).WaitFor(backend)
    .WithExternalHttpEndpoints();
```

## 2. AppHost Variants

The project provides **three AppHost variants** for different deployment scenarios:

| Project | Use Case | What's included |
|---------|----------|-----------------|
| `Agitprop.AppHost` | Full distributed app | postgres + newsfeedDb + nlpService + consumer + rss-reader + backend + frontend + rabbitmq + dashboard |
| `Agitprop.AppHost.App` | App-only deployment | postgres + newsfeedDb + backend + frontend + dashboard |
| `Agitprop.AppHost.Worker` | Worker-only deployment | nlpService + consumer + dashboard |

### Run a specific variant:

```bash
# Full app
dotnet run --project Agitprop.AppHost/Agitprop.AppHost.csproj

# App variant
dotnet run --project Agitprop.AppHost.App/Agitprop.AppHost.App.csproj

# Worker variant
dotnet run --project Agitprop.AppHost.Worker/Agitprop.AppHost.Worker.csproj
```

## 3. Docker & Image Publishing

Images are published to **GHCR** (`ghcr.io/fortyfei/agitprop`) via `docker-bake.hcl` and GitHub Actions.

### Build & Push manually:

```bash
# Login to GHCR
echo $GITHUB_TOKEN | docker login ghcr.io -u fortyfei --password-stdin

# Build all images
docker buildx bake -f docker-bake.hcl

# Publish
docker buildx bake -f docker-bake.hcl --push
```

### Build a single service image:

```bash
docker build -t agitprop/consumer:latest -f Agitprop.Scraper.Consumer/Dockerfile .
```

## 4. CI/CD Pipeline

### GitHub Actions: `aspire-publish.yml`

The workflow (`.github/workflows/aspire-publish.yml`) runs on push to `main` and:

1. Restores .NET solution.
2. Builds Aspire artifacts (`dotnet build` + `dotnet publish`).
3. Bakes Docker images via `docker buildx bake -f docker-bake.hcl --push`.
4. Pushes images to `ghcr.io/fortyfei/agitprop/*`.

### Environment Variables for CI:

```yaml
env:
  REGISTRY: ghcr.io
  IMAGE_NAMESPACE: fortyfei
  DOCKER_BUILDKIT: 1
```

## 5. Aspire CLI Commands

```bash
# Start the app
aspire start

# Start with isolated mode (randomized ports)
aspire start --isolated

# Watch for changes
aspire start --watch

# Stop the app
aspire stop

# List running AppHosts
aspire ps

# Show resources
aspire resource

# Wait for resources
aspire wait

# View logs
aspire logs

# Open dashboard
aspire dashboard

# Check health
aspire doctor

# Update Aspire CLI
aspire update --self
```

## 6. Production Deployment Checklist

- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Connection strings set via environment variables (not appsettings)
- [ ] `ApplyMigrationsAtStartup=false` (run migrations manually)
- [ ] GHCR credentials configured for image pull
- [ ] OTLP collector endpoint configured (`OTEL_EXPORTER_OTLP_ENDPOINT`)
- [ ] RabbitMQ credentials rotated from `guest:guest`
- [ ] PostgreSQL credentials rotated from defaults
- [ ] Proxy providers configured (if using `UseProxies=true`)
- [ ] Health checks accessible for orchestration (k8s liveness/readiness)
- [ ] Persistent volumes for PostgreSQL data
- [ ] Network policies allow RabbitMQ AMQP traffic between services

## 7. Kubernetes Deployment (Future)

Aspire supports `Aspire.Hosting.Kubernetes` for k8s manifest generation:

```bash
aspire do --operation publish --output-path ./manifests --project Agitprop.AppHost/Agitprop.AppHost.csproj
```

This generates Kubernetes manifests for all services with proper service discovery and dependencies.

## 8. Cloud Deployment (Azure Container Apps)

Aspire can deploy to Azure Container Apps:

```bash
az containerapp up \
  --resource-group agitprop \
  --environment agitprop-env \
  --image ghcr.io/fortyfei/agitprop/consumer:latest
```

## 9. Health Check Endpoints

| Service | Health Check | Path |
|---------|-------------|------|
| Consumer | Aspire built-in | `/health` |
| Web API | Aspire built-in | `/health` |
| Web Client | Aspire built-in | `/health` |
| NLP Service | Custom FastAPI | `GET /health` |
| RabbitMQ | Management plugin | `http://localhost:15672` |
| PostgreSQL | pgAdmin | `http://localhost:5050` |