using Microsoft.Extensions.Configuration;

using Projects;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var compose = builder.AddDockerComposeEnvironment("agitprop")
                     .WithDashboard(d => d.WithHostPort(18888));

        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        
        var messaging = builder.AddConnectionString("messaging");
        var newsfeedDb = builder.AddConnectionString("newsfeed");

        var nlpService = builder.AddUvicornApp("nlpservice", "../Agitprop.Scraper.NLPService", "app:app")
                        .WithHttpHealthCheck("/health")
                        .WithEnvironment("Reload", "True")
                        .WithEnvironment("LOG_LEVEL", "debug")
                        .WithOtlpExporter()
                        .PublishAsDockerComposeService((resource, service) => { service.Name = "nlpservice"; });

        var consumer = builder.AddProject<Agitprop_Scraper_Consumer>("consumer")
                              .WaitFor(newsfeedDb)
                              .WithReference(newsfeedDb)
                              .WaitFor(messaging)
                              .WithReference(messaging)
                              .WaitFor(nlpService)
                              .WithReference(nlpService)
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) => { service.Name = "consumer"; });

        builder.Build().Run();
    }
}
