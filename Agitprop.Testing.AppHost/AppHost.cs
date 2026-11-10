using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageRegistry("ghcr.io")
    .WithImage("pgmq/pg17-pgmq")
    .WithImageTag("v1.13.1");
var newsfeed = postgres.AddDatabase("newsfeed");

builder.AddProject<Agitprop_Web_Api>("backend")
    .WaitFor(newsfeed)
    .WithReference(newsfeed)
    .WithEnvironment("ApplyMigrationsAtStartup", "true");

builder.Build().Run();