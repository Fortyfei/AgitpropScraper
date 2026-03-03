using Projects;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var compose = builder.AddDockerComposeEnvironment("Agitprop");

        var messaging = builder.AddRabbitMQ("messaging")
                               .WithManagementPlugin(4242)
                               .WithOtlpExporter()
                               .PublishAsDockerComposeService((resource, service) =>
                               {
                                   service.Name = "messaging";
                               });

        var postgres = builder.AddPostgres("postgres")
                              .WithDataVolume(isReadOnly: false)
                              .WithPgAdmin(pgAdmin =>
                              {
                                  pgAdmin.WithHostPort(5050);
                                  pgAdmin.WithImageTag("latest");
                              })
                              .WithExternalHttpEndpoints()
                              .WithLifetime(ContainerLifetime.Persistent)
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) =>
                               {
                                   service.Name = "postgres";
                               });

        var newsfeedDb = postgres.AddDatabase("newsfeed");

        var nlpService = builder.AddUvicornApp("nlpService", "../Agitprop.Scraper.NLPService", "app:app")
                                .WithHttpHealthCheck("/health")
                                .WithEnvironment("Reload", "True")
                                .WithEnvironment("LOG_LEVEL", "debug")
                                .WithOtlpExporter()
                                .PublishAsDockerComposeService((resource, service) =>
                                {
                                    service.Name = "nlp-service";
                                });

        var consumer = builder.AddProject<Agitprop_Scraper_Consumer>("consumer")
                              .WaitFor(newsfeedDb).WithReference(newsfeedDb)
                              .WaitFor(messaging).WithReference(messaging)
                              .WaitFor(nlpService).WithReference(nlpService)
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) =>
                              {
                                  service.Name = "consumer";
                              });

        var rssReader = builder.AddProject<Agitprop_Scraper_RssFeedReader>("rss-feed-reader")
                               .WaitFor(messaging).WithReference(messaging)
                               .WaitFor(consumer)
                               .WithOtlpExporter()
                               .PublishAsDockerFile()
                               .PublishAsDockerComposeService((resource, service) =>
                               {
                                   service.Name = "rss-reader";
                               });

        var backend = builder.AddProject<Agitprop_Web_Api>("backend")
                             .WaitFor(newsfeedDb).WithReference(newsfeedDb)
                             .WaitFor(messaging).WithReference(messaging)
                             .WithOtlpExporter()
                             .PublishAsDockerFile()
                             .PublishAsDockerComposeService((resource, service) =>
                             {
                                 service.Name = "backend-api";
                             });

        var frontend = builder.AddJavaScriptApp("angular", "../Agitprop.Web.Client")
            .WithReference(backend).WaitFor(backend)
            // .WithHttpEndpoint(port: 4200)
            .WithExternalHttpEndpoints()
            .PublishAsDockerComposeService((resource, service) =>
            {
                service.Name = "frontend-angular";
            });

        builder.Build().Run();
    }
}