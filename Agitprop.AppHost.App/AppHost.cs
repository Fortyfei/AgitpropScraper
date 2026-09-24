using Microsoft.Extensions.Hosting;

using Projects;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var compose = builder.AddDockerComposeEnvironment("agitprop");

        var postgres = builder.AddPostgres("postgres").WithDataVolume(isReadOnly: false);
        var newsfeedDb = postgres.AddDatabase("newsfeed");

        var backend = builder.AddProject<Agitprop_Web_Api>("backend")
                             .WaitFor(newsfeedDb)
                             .WithReference(newsfeedDb)
                             .WithOtlpExporter();

        var frontend = builder.AddProject<Agitprop_Web_Client>("frontend")
                              .WaitFor(backend)
                              .WithReference(backend)
                              .WithOtlpExporter();

        builder.Build().Run();
    }
}
