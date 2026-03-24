using Projects;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var registry = builder.AddContainerRegistry("ghcr", "ghcr.io", "fortyfei/agitprop");

        var compose = builder.AddDockerComposeEnvironment("Agitprop")
                             .WithDashboard(d => d.WithHostPort(18888));

        var messaging = builder.AddRabbitMQ("messaging")
                               .WithManagementPlugin(15672)
                               .WithExternalHttpEndpoints()
                               .WithOtlpExporter()
                               .PublishAsDockerComposeService((resource, service) => { service.Name = "messaging"; });

        var postgres = builder.AddPostgres("postgres")
                              .WithDataVolume(isReadOnly: false)
                              .WithPgAdmin(pgAdmin => { pgAdmin.WithHostPort(5050); pgAdmin.WithImageTag("latest"); })
                              .WithExternalHttpEndpoints()
                              .WithLifetime(ContainerLifetime.Persistent)
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) => { service.Name = "postgres"; });
        var newsfeedDb = postgres.AddDatabase("newsfeed");

        var nlpService = builder.AddUvicornApp("nlpservice", "../Agitprop.Scraper.NLPService", "app:app")
                                .WithHttpHealthCheck("/health")
                                .WithEnvironment("Reload", "True")
                                .WithEnvironment("LOG_LEVEL", "debug")
                                .WithOtlpExporter()
                                .PublishAsDockerComposeService((resource, service) => { service.Name = "nlpservice"; })
                                .WithContainerRegistry(registry);

        var consumer = builder.AddProject<Agitprop_Scraper_Consumer>("consumer")
                              .WaitFor(newsfeedDb)
                              .WithReference(newsfeedDb)
                              .WaitFor(messaging)
                              .WithReference(messaging)
                              .WaitFor(nlpService)
                              .WithReference(nlpService)
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) => { service.Name = "consumer"; })
                              .WithContainerRegistry(registry);

        var rssReader = builder.AddProject<Agitprop_Scraper_RssFeedReader>("rss-feed-reader")
                               .WaitFor(messaging)
                               .WithReference(messaging)
                               .WaitFor(consumer)
                               .WithOtlpExporter()
                               .PublishAsDockerComposeService((resource, service) => { service.Name = "rssReader"; });

        var backend = builder.AddProject<Agitprop_Web_Api>("backend")
                             .WaitFor(newsfeedDb)
                             .WithReference(newsfeedDb)
                             .WaitFor(messaging)
                             .WithReference(messaging)
                             .WithOtlpExporter()
                             .PublishAsDockerComposeService((resource, service) => { service.Name = "backend"; })
                             .WithContainerRegistry(registry);

        var frontend = builder.AddJavaScriptApp("angular", "../Agitprop.Web.Client")
                              .WithReference(backend)
                              .WaitFor(backend)
                              .WithHttpEndpoint(port: 4200)
                              .WithExternalHttpEndpoints()
                              .PublishAsDockerComposeService((resource, service) => { service.Name = "frontend"; })
                              .WithContainerRegistry(registry);

        builder.Build().Run();
    }
}