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

        var newsfeedDb = builder.AddConnectionString("newsfeed");

        var backend = builder.AddProject<Agitprop_Web_Api>("backend")
                             .WaitFor(newsfeedDb)
                             .WithReference(newsfeedDb)
                             .WithOtlpExporter()
                             .PublishAsDockerComposeService((resource, service) => { service.Name = "backend"; });

        var frontend = builder.AddProject<Agitprop_Web_Client>("frontend")
                              .WaitFor(backend)
                              .WithReference(backend)
                              .WithExternalHttpEndpoints()
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) => { service.Name = "frontend"; });

        builder.Build().Run();
    }
}
