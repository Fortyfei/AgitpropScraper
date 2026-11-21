var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageRegistry("ghcr.io")
    .WithImage("pgmq/pg17-pgmq")
    .WithImageTag("v1.13.1");
var newsfeed = postgres.AddDatabase("newsfeed");

builder.AddDotnetProject("web", "../Agitprop.Web/Agitprop.Web.csproj")
    .WaitFor(newsfeed)
    .WithReference(newsfeed)
    .WithEnvironment("ApplyMigrationsAtStartup", "true")
    .WithEnvironment("HealthEndpoints__Enabled", "true");

builder.Build().Run();