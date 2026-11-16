using Agitprop.Core.Interfaces;
using Agitprop.Data;
using Agitprop.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Agitprop.UnitTests;

public class PostgresRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=newsfeed;Username=test;Password=test";

    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:newsfeed"] = ConnectionString
        });

        return builder;
    }

    [Test]
    public void AddPgmqClient_RegistersDataSourceAndQueueClient()
    {
        var builder = CreateBuilder();
        builder.AddPgmqClient("newsfeed");

        using var host = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(host.Services.GetRequiredService<NpgsqlDataSource>(), Is.Not.Null);
            Assert.That(host.Services.GetRequiredService<IPgmqClient>(), Is.Not.Null);
        });
    }

    [Test]
    public void AddNewsfeedDB_RegistersDbContextAndRepository()
    {
        var builder = CreateBuilder();
        builder.AddNewsfeedDB();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(scope.ServiceProvider.GetRequiredService<AppDbContext>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetRequiredService<INewsfeedDB>(), Is.Not.Null);
        });
    }

    [Test]
    public void AddNewsfeedRepositories_CanBeCombinedWithAddNewsfeedDB()
    {
        var builder = CreateBuilder();
        builder.AddNewsfeedRepositories();
        builder.AddNewsfeedDB();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(scope.ServiceProvider.GetRequiredService<AppDbContext>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetRequiredService<IEntityRepository>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetRequiredService<INewsfeedDB>(), Is.Not.Null);
        });
    }
}
