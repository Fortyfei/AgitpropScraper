using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17");
var newsfeed = postgres.AddDatabase("newsfeed");

builder.AddProject<Agitprop_Web_Api>("backend")
    .WaitFor(newsfeed)
    .WithReference(newsfeed)
    .WithEnvironment("ApplyMigrationsAtStartup", "true");

builder.Build().Run();