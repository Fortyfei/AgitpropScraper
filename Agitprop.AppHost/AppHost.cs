using Agitprop.AppHost;

using Projects;

internal class Program
{
    private const int WebPort = 8080;

    // The published .NET images run as $APP_UID and /app belongs to that user.
    private const int AppImageUserId = 1654;

    private const string ModelDirectory = "ner-model";

    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var publishMode = builder.ExecutionContext.IsPublishMode;

        var registry = builder.AddContainerRegistry("ghcr", "ghcr.io", "fortyfei/agitprop");

        var compose = builder.AddDockerComposeEnvironment("agitprop");
        if (publishMode)
        {
            // The dashboard is a local development aid; a cheap VPS does not run it.
            compose.WithDashboard(enabled: false);
        }
        else
        {
            compose.WithDashboard(dashboard => dashboard.WithHostPort(18888));
        }

        var postgresPassword = builder.AddParameter("postgres-password", secret: true);
        var appDbPassword = builder.AddParameter("app-db-password", secret: true);

        var postgres = builder.AddPostgres("postgres", password: postgresPassword)
                              .WithImageRegistry("ghcr.io")
                              .WithImage("pgmq/pg17-pgmq")
                              .WithImageTag("v1.13.1")
                              // Pin the name: the generated one carries an AppHost hash, so any
                              // model edit would otherwise leave the data behind in a new volume.
                              .WithDataVolume("agitprop-postgres-data")
                              .WithLifetime(ContainerLifetime.Persistent)
                              .WithOtlpExporter()
                              .WithComposeHealthCheck("CMD-SHELL", "pg_isready -U postgres -d newsfeed")
                              .LimitComposeMemory("1G")
                              .HardenComposeService();

        if (!publishMode)
        {
            postgres.WithPgAdmin(pgAdmin =>
                    {
                        pgAdmin.WithHostPort(5050);
                        pgAdmin.WithImageTag("latest");
                    })
                    .WithEndpoint(scheme: "tcp", port: 5432, targetPort: 5432, isExternal: true);
        }

        // Published stacks have no database port: the only ways in are
        // `docker compose exec postgres psql` or an SSH tunnel to the container network.
        var newsfeedDb = postgres.AddDatabase("newsfeed");

        var consumer = builder.AddProject<Agitprop_Scraper_Consumer>("consumer")
                              .WaitFor(newsfeedDb)
                              .WithOtlpExporter()
                              .WithContainerRegistry(registry)
                              .WithEnvironmentAwareImagePush()
                              .LimitComposeMemory("1200M")
                              .WatchtowerManaged()
                              .HardenComposeService();

        var rssReader = builder.AddProject<Agitprop_Scraper_RssFeedReader>("rss-feed-reader")
                               .WaitFor(newsfeedDb)
                               .WaitFor(consumer)
                               .WithOtlpExporter()
                               .WithContainerRegistry(registry)
                               .WithEnvironmentAwareImagePush()
                               .LimitComposeMemory("256M")
                               .WatchtowerManaged()
                               .HardenComposeService();

        var web = builder.AddProject<Agitprop_Web>("web")
                         .WaitFor(newsfeedDb)
                         .WithOtlpExporter()
                         .WithContainerRegistry(registry)
                         .WithEnvironmentAwareImagePush()
                         .LimitComposeMemory("512M")
                         .WatchtowerManaged()
                         .HardenComposeService();

        if (publishMode)
        {
            // The bootstrap script creates the app role and its database, so the app never runs
            // as the container superuser: COPY ... FROM PROGRAM would otherwise let a compromised
            // query execute programs on the database host. It runs once, when the data volume is
            // empty; an existing volume needs the role created by hand (see .docs/deployment.md).
            postgres.WithInitFiles("postgres-init")
                    .WithEnvironment("AGITPROP_DB_PASSWORD", appDbPassword);

            var appDbConnectionString = ReferenceExpression.Create(
                $"Host=postgres;Port=5432;Username=agitprop;Password={appDbPassword};Database=newsfeed");

            consumer.WithEnvironment("ConnectionStrings__newsfeed", appDbConnectionString);
            rssReader.WithEnvironment("ConnectionStrings__newsfeed", appDbConnectionString);
            web.WithEnvironment("ConnectionStrings__newsfeed", appDbConnectionString);

            consumer.WaitForHealthyDependency("postgres");

            // The NER model is ~440 MB and is checksum-verified on every start, so it belongs on a
            // volume: a container recreate would otherwise download it again, and an unreachable
            // Hugging Face would leave the worker in a crash loop. A fresh Docker volume belongs to
            // root, while the app runs as $APP_UID, so a one-shot container hands the volume over
            // before the consumer starts.
            var modelVolume = "agitprop-ner-model";
            var modelPath = $"/app/{ModelDirectory}";

            var modelOwnership = builder.AddContainer("model-ownership", "mcr.microsoft.com/dotnet/runtime-deps", "10.0")
                                        .WithEntrypoint("sh")
                                        .WithArgs("-c", $"chown {AppImageUserId}:{AppImageUserId} /model")
                                        .WithVolume(modelVolume, "/model");

            consumer.WaitForCompletion(modelOwnership)
                    .WithPublishedVolume(modelVolume, modelPath)
                    .WithEnvironment("NLP__ModelDirectory", modelPath);

            rssReader.WaitForHealthyDependency("postgres");

            // One fixed container port, because the Cloudflare public hostname targets
            // http://web:8080 and must not depend on a value filled in at release time.
            web.WithoutHttpsEndpoint()
               .WithHttpEndpoint(targetPort: WebPort, name: "http")
               // Migrations are the web app's job in a published stack and migrating is idempotent.
               .WithEnvironment("ApplyMigrationsAtStartup", "true")
               // /health and /alive let an uptime monitor see the app without a shell on the host.
               .WithEnvironment("HealthEndpoints__Enabled", "true")
               // The runtime image ships neither curl nor wget, but bash can open a socket.
               .WithComposeHealthCheck("CMD", "bash", "-c", $"exec 3<>/dev/tcp/127.0.0.1/{WebPort}")
               .WaitForHealthyDependency("postgres");

            // Public entry point: cloudflared dials out to Cloudflare, so the host needs
            // neither a public IP nor open 80/443 ports. The public hostname is configured
            // in the Cloudflare dashboard and targets http://web:8080.
            var tunnelToken = builder.AddParameter("tunnel-token", secret: true);

            builder.AddContainer("cloudflared", "cloudflare/cloudflared", "2026.11.0")
                   .WithArgs("tunnel", "--no-autoupdate", "run")
                   .WithEnvironment("TUNNEL_TOKEN", tunnelToken)
                   .WithReference(web)
                   .WaitFor(web)
                   .WaitForHealthyDependency("web")
                   .LimitComposeMemory("128M")
                   .HardenComposeService();

            // Deploys are pulled by the host: the release job moves the "stable" tag on every
            // merge, Watchtower notices the new digest and recreates only the labelled app
            // containers. It gets the Docker socket, which is root-equivalent on this host, so it
            // is the one container allowed to have it and it touches nothing unlabelled.
            builder.AddContainer("watchtower", "containrrr/watchtower", "1.7.1")
                   .WithArgs("--label-enable", "--cleanup", "--interval", "300", "--stop-timeout", "120")
                   .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
                   .LimitComposeMemory("64M")
                   .PublishAsDockerComposeService((_, service) => service.CapDrop = ["ALL"])
                   .HardenComposeService();
        }
        else
        {
            // Locally the AppHost creates the database itself, so the app keeps the container
            // credentials; the role above exists only in a published stack.
            consumer.WithReference(newsfeedDb);
            rssReader.WithReference(newsfeedDb);

            web.WithExternalHttpEndpoints()
               .WithReference(newsfeedDb);
        }

        builder.Build().Run();
    }
}
