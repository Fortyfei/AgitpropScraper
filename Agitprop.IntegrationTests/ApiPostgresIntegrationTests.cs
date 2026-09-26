using System.Net;
using System.Text.Json;
using Agitprop.Sinks.Newsfeed.Database;
using Agitprop.Sinks.Newsfeed.Database.Models;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Projects;

namespace Agitprop.IntegrationTests;

[NonParallelizable]
public class ApiPostgresIntegrationTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private IAsyncDisposable? _application;
    private HttpClient? _client;
    private Guid _entityId;

    [OneTimeSetUp]
    public async Task StartApplication()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Agitprop_Testing_AppHost>(timeout.Token);
        var application = await builder.BuildAsync(timeout.Token)
            .WaitAsync(timeout.Token);
        _application = application;

        await application.StartAsync(timeout.Token)
            .WaitAsync(timeout.Token);
        await application.ResourceNotifications
            .WaitForResourceHealthyAsync("backend", timeout.Token)
            .WaitAsync(timeout.Token);

        _entityId = await SeedDatabaseAsync(application, timeout.Token);
        _client = application.CreateHttpClient("backend");
    }

    [OneTimeTearDown]
    public async Task StopApplication()
    {
        _client?.Dispose();
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }

    [Test]
    public async Task BrowseEntitiesWithSearch_QueriesMigratedPostgresDatabase()
    {
        using var response = await _client!.GetAsync(
            "/api/Entities?from=2026-01-01&to=2026-01-31&search=integration");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items");
        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(items.GetArrayLength(), Is.EqualTo(1));
            Assert.That(items[0].GetProperty("id").GetGuid(), Is.EqualTo(_entityId));
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("Aspire integration entity"));
            Assert.That(items[0].GetProperty("mentionCount").GetInt32(), Is.EqualTo(1));
        });
    }

    private static async Task<Guid> SeedDatabaseAsync(
        Aspire.Hosting.DistributedApplication application,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(await application.GetConnectionStringAsync("newsfeed", cancellationToken))
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);

        var article = new PostgresArticle
        {
            Id = Guid.NewGuid(),
            Title = "Aspire test article",
            Url = "https://integration.test/article",
            PublishedTime = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)
        };
        var entity = new PostgresEntity
        {
            Id = Guid.NewGuid(),
            Name = "Aspire integration entity",
            Type = "ORG"
        };
        db.Articles.Add(article);
        db.Entities.Add(entity);
        db.Mentions.Add(new PostgresMention
        {
            Article = article,
            Entity = entity
        });

        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}