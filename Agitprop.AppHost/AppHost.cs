using Agitprop.AppHost;

using Projects;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var registry = builder.AddContainerRegistry("ghcr", "ghcr.io", "fortyfei/agitprop");

        var compose = builder.AddDockerComposeEnvironment("agitprop")
                             .WithDashboard(d => d.WithHostPort(18888));

        var messaging = builder.AddRabbitMQ("messaging")
                               .WithManagementPlugin(15672)
                               .WithExternalHttpEndpoints()
                               .WithDataVolume()
                               .WithLifetime(ContainerLifetime.Persistent)
                               .WithOtlpExporter();

        var postgres = builder.AddPostgres("postgres")
                              .WithImageTag("17")
                              .WithDataVolume(isReadOnly: false)
                              .WithPgAdmin(pgAdmin => { pgAdmin.WithHostPort(5050); pgAdmin.WithImageTag("latest"); })
                              .WithEndpoint(scheme: "tcp", port: 5432, targetPort: 5432, isExternal: true)
                              .WithLifetime(ContainerLifetime.Persistent)
                              .WithOtlpExporter();

        var newsfeedDb = postgres.AddDatabase("newsfeed");

        var consumer = builder.AddProject<Agitprop_Scraper_Consumer>("consumer")
                              .WaitFor(newsfeedDb)
                              .WithReference(newsfeedDb)
                              .WaitFor(messaging)
                              .WithReference(messaging)
                              .WithOtlpExporter()
                              .WithContainerRegistry(registry)
                              .WithEnvironmentAwareImagePush();

        var rssReader = builder.AddProject<Agitprop_Scraper_RssFeedReader>("rss-feed-reader")
                               .WaitFor(messaging)
                               .WithReference(messaging)
                               .WaitFor(consumer)
                               .WithOtlpExporter()
                               .WithEnvironmentAwareImagePush();

        var backend = builder.AddProject<Agitprop_Web_Api>("backend")
                             .WaitFor(newsfeedDb)
                             .WithReference(newsfeedDb)
                             .WaitFor(messaging)
                             .WithReference(messaging)
                             .WithOtlpExporter()
                             .WithContainerRegistry(registry)
                             .WithEnvironmentAwareImagePush();

        var frontend = builder.AddProject<Agitprop_Web_Client>("frontend")
                              .WaitFor(backend)
                              .WithReference(backend)
                              .WithExternalHttpEndpoints()
                              .WithOtlpExporter()
                              .WithContainerRegistry(registry)
                              .WithEnvironmentAwareImagePush();

        builder.Build().Run();
    }
}
