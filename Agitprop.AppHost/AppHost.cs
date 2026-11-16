using Agitprop.AppHost;

using Projects;

internal class Program
{
    private const int WebPort = 8080;

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

        var postgres = builder.AddPostgres("postgres", password: postgresPassword)
                              .WithImageRegistry("ghcr.io")
                              .WithImage("pgmq/pg17-pgmq")
                              .WithImageTag("v1.13.1")
                              .WithDataVolume(isReadOnly: false)
                              .WithLifetime(ContainerLifetime.Persistent)
                              .WithOtlpExporter()
                              .WithComposeHealthCheck("CMD-SHELL", "pg_isready -U postgres -d newsfeed")
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
                              .WithReference(newsfeedDb)
                              .WithOtlpExporter()
                              .WithContainerRegistry(registry)
                              .WithEnvironmentAwareImagePush()
                              .HardenComposeService();

        var rssReader = builder.AddProject<Agitprop_Scraper_RssFeedReader>("rss-feed-reader")
                               .WaitFor(newsfeedDb)
                               .WithReference(newsfeedDb)
                               .WaitFor(consumer)
                               .WithOtlpExporter()
                               .WithContainerRegistry(registry)
                               .WithEnvironmentAwareImagePush()
                               .HardenComposeService();

        var web = builder.AddProject<Agitprop_Web>("web")
                         .WaitFor(newsfeedDb)
                         .WithReference(newsfeedDb)
                         .WithOtlpExporter()
                         .WithContainerRegistry(registry)
                         .WithEnvironmentAwareImagePush()
                         .HardenComposeService();

        if (publishMode)
        {
            consumer.WaitForHealthyDependency("postgres");
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
                   .HardenComposeService();
        }
        else
        {
            web.WithExternalHttpEndpoints();
        }

        builder.Build().Run();
    }
}
