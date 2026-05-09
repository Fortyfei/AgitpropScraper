using Projects;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        var messaging = builder.AddConnectionString("messaging", "amqp://guest:guest@localhost:5672");

        var nlpService = builder.AddUvicornApp("nlpservice", "../Agitprop.Scraper.NLPService", "app:app")
                                .WithHttpHealthCheck("/health")
                                .WithEnvironment("Reload", "True")
                                .WithEnvironment("LOG_LEVEL", "debug")
                                .WithOtlpExporter()
                                .PublishAsDockerComposeService((resource, service) => { service.Name = "nlpservice"; });

        var consumer = builder.AddProject<Agitprop_Scraper_Consumer>("consumer")
                              .WaitFor(messaging)
                              .WithReference(messaging)
                              .WaitFor(nlpService)
                              .WithReference(nlpService)
                              .WithOtlpExporter()
                              .PublishAsDockerComposeService((resource, service) => { service.Name = "consumer"; });

        builder.Build().Run();
    }
}
