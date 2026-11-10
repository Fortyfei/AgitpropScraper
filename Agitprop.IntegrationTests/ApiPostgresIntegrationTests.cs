using System.Net;
using System.Diagnostics;
using System.Text.Json;
using Agitprop.Data;
using Agitprop.Data.Models;
using Agitprop.Messaging;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Projects;

namespace Agitprop.IntegrationTests;

[NonParallelizable]
public class ApiPostgresIntegrationTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private IAsyncDisposable? _application;
    private Aspire.Hosting.DistributedApplication? _distributedApplication;
    private HttpClient? _client;
    private Guid _entityId;
    private Guid _coMentionEntityId;

    [OneTimeSetUp]
    public async Task StartApplication()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Agitprop_Testing_AppHost>(timeout.Token);
        var application = await builder.BuildAsync(timeout.Token)
            .WaitAsync(timeout.Token);
        _application = application;
        _distributedApplication = application;

        await application.StartAsync(timeout.Token)
            .WaitAsync(timeout.Token);
        await application.ResourceNotifications
            .WaitForResourceHealthyAsync("backend", timeout.Token)
            .WaitAsync(timeout.Token);

        (_entityId, _coMentionEntityId) = await SeedDatabaseAsync(application, timeout.Token);
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
            Assert.That(items[0].GetProperty("mentionCount").GetInt32(), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task CoMentions_ReturnSharedArticleCountsFromMigratedPostgresDatabase()
    {
        using var response = await _client!.GetAsync(
            $"/api/entities/{_entityId}/co-mentions?from=2026-01-01&to=2026-01-31");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var relatedEntity = body.RootElement.GetProperty("entities")[0];
        Assert.Multiple(() =>
        {
            Assert.That(relatedEntity.GetProperty("entityId").GetGuid(), Is.EqualTo(_coMentionEntityId));
            Assert.That(relatedEntity.GetProperty("entityName").GetString(), Is.EqualTo("Co-mentioned entity"));
            Assert.That(relatedEntity.GetProperty("sharedArticleCount").GetInt32(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SourceEntityCoverage_UsesPublisherArticleTotalsAsDenominator()
    {
        using var response = await _client!.GetAsync(
            "/api/analytics/source-entities?from=2026-01-01&to=2026-01-31");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sources = body.RootElement.GetProperty("sources");
        var integrationSource = sources[0];
        var entities = integrationSource.GetProperty("entities");
        Assert.Multiple(() =>
        {
            Assert.That(integrationSource.GetProperty("domain").GetString(), Is.EqualTo("integration.test"));
            Assert.That(integrationSource.GetProperty("articleCount").GetInt32(), Is.EqualTo(3));
            Assert.That(entities[0].GetProperty("entityId").GetGuid(), Is.EqualTo(_entityId));
            Assert.That(entities[0].GetProperty("articleCount").GetInt32(), Is.EqualTo(3));
            Assert.That(entities[0].GetProperty("percentOfSourceArticles").GetDouble(), Is.EqualTo(100));
            Assert.That(entities[1].GetProperty("entityId").GetGuid(), Is.EqualTo(_coMentionEntityId));
            Assert.That(entities[1].GetProperty("articleCount").GetInt32(), Is.EqualTo(2));
            Assert.That(entities[1].GetProperty("percentOfSourceArticles").GetDouble(), Is.EqualTo(200.0 / 3).Within(0.01));
            Assert.That(sources[1].GetProperty("domain").GetString(), Is.EqualTo("publisher-two.test"));
        });
    }

    [Test]
    public async Task ArticleFeed_FiltersByPublisherEntityAndSearch()
    {
        var feed = await GetJsonAsync("/api/articles/feed?from=2026-01-01&to=2026-01-31");
        var items = feed.GetProperty("items");
        Assert.Multiple(() =>
        {
            Assert.That(feed.GetProperty("totalCount").GetInt32(), Is.EqualTo(4));
            Assert.That(items[0].GetProperty("domain").GetString(), Is.EqualTo("publisher-two.test"));
            Assert.That(items[3].GetProperty("domain").GetString(), Is.EqualTo("integration.test"));
            Assert.That(items[3].GetProperty("entities").GetArrayLength(), Is.EqualTo(2));
        });

        var byDomain = await GetJsonAsync("/api/articles/feed?from=2026-01-01&to=2026-01-31&domain=integration.test&pageSize=2");
        Assert.Multiple(() =>
        {
            Assert.That(byDomain.GetProperty("totalCount").GetInt32(), Is.EqualTo(3));
            Assert.That(byDomain.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
        });

        var byEntity = await GetJsonAsync($"/api/articles/feed?from=2026-01-01&to=2026-01-31&entityId={_coMentionEntityId}");
        Assert.That(byEntity.GetProperty("totalCount").GetInt32(), Is.EqualTo(2));

        var bySearch = await GetJsonAsync("/api/articles/feed?from=2026-01-01&to=2026-01-31&search=SECOND");
        Assert.That(bySearch.GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task DailyCounts_ReturnsZeroFilledDays()
    {
        var daily = await GetJsonAsync("/api/articles/daily?from=2026-01-14&to=2026-01-19");
        var counts = daily.GetProperty("days").EnumerateArray()
            .Select(day => day.GetProperty("count").GetInt32())
            .ToArray();

        Assert.That(counts, Is.EqualTo(new[] { 0, 1, 1, 1, 1, 0 }));
    }

    [Test]
    public async Task FirstPublishers_RanksPublishersByEarliestArticle()
    {
        var result = await GetJsonAsync($"/api/articles/first-publishers?from=2026-01-01&to=2026-01-31&entityId={_entityId}");
        var publishers = result.GetProperty("publishers");
        Assert.Multiple(() =>
        {
            Assert.That(publishers[0].GetProperty("domain").GetString(), Is.EqualTo("integration.test"));
            Assert.That(publishers[0].GetProperty("articleCount").GetInt32(), Is.EqualTo(3));
            Assert.That(publishers[1].GetProperty("domain").GetString(), Is.EqualTo("publisher-two.test"));
        });
    }

    [Test]
    public async Task PgmqClient_SendsReadsMovesAndAcknowledgesMessages()
    {
        var connectionString = await _distributedApplication!.GetConnectionStringAsync(
            "newsfeed",
            CancellationToken.None)
            ?? throw new InvalidOperationException("The newsfeed connection string was not available.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var pgmqClient = new PgmqClient(dataSource);
        var queueName = $"it_{Guid.NewGuid():N}";
        var failedQueueName = $"{queueName}_error";
        await pgmqClient.EnsureQueuesAsync(CancellationToken.None, queueName, failedQueueName);

        using var producerActivity = new Activity("integration queue producer")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var sentCount = await pgmqClient.SendBatchAsync(
            queueName,
            new[] { new QueueTestMessage("first"), new QueueTestMessage("second") });
        var firstRead = await pgmqClient.ReadAsync<QueueTestMessage>(
            queueName,
            visibilityTimeoutSeconds: 60,
            quantity: 1);
        Assert.That(sentCount, Is.EqualTo(2));
        Assert.That(firstRead, Has.Count.EqualTo(1));
        Assert.That(firstRead[0].Headers?["traceparent"], Is.EqualTo(producerActivity.Id));

        await pgmqClient.SetVisibilityTimeoutAsync(
            queueName,
            firstRead[0].MessageId,
            delaySeconds: 0);
        var retried = await pgmqClient.ReadAsync<QueueTestMessage>(
            queueName,
            visibilityTimeoutSeconds: 60,
            quantity: 1);
        Assert.That(retried, Has.Count.EqualTo(1));
        Assert.That(retried[0].ReadCount, Is.EqualTo(2));

        await pgmqClient.MoveAsync(
            queueName,
            failedQueueName,
            retried[0].MessageId,
            retried[0].Message);

        var remaining = await pgmqClient.ReadAsync<QueueTestMessage>(
            queueName,
            visibilityTimeoutSeconds: 60,
            quantity: 10);
        var failed = await pgmqClient.ReadAsync<QueueTestMessage>(
            failedQueueName,
            visibilityTimeoutSeconds: 60,
            quantity: 10);
        Assert.Multiple(() =>
        {
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].Message.Value, Is.EqualTo("second"));
            Assert.That(failed, Has.Count.EqualTo(1));
            Assert.That(failed[0].Message.Value, Is.EqualTo("first"));
        });

        await pgmqClient.DeleteAsync(queueName, remaining[0].MessageId);
        await pgmqClient.DeleteAsync(failedQueueName, failed[0].MessageId);
    }

    [Test]
    public async Task PgmqPolling_SuppressesNpgsqlInstrumentationOnlyWithinScope()
    {
        var connectionString = await _distributedApplication!.GetConnectionStringAsync("newsfeed", CancellationToken.None)
            ?? throw new InvalidOperationException("The newsfeed connection string was not available.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var client = new PgmqClient(dataSource);
        var queueName = $"it_{Guid.NewGuid():N}";
        await client.EnsureQueuesAsync(CancellationToken.None, queueName);

        var spans = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("Npgsql")
            .AddProcessor(new RecordingActivityProcessor(spans))
            .Build();

        await client.ReadAsync<QueueTestMessage>(queueName, 60, 1);
        using (SuppressInstrumentationScope.Begin())
        {
            await client.ReadAsync<QueueTestMessage>(queueName, 60, 1);
        }
        await client.ReadAsync<QueueTestMessage>(queueName, 60, 1);

        var readSpans = spans.Where(span =>
            span.GetTagItem("db.query.text") is string sql && sql.Contains("pgmq.read", StringComparison.Ordinal));
        Assert.That(readSpans.Count(), Is.EqualTo(2));
    }

    private sealed class RecordingActivityProcessor(List<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => spans.Add(data);
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await _client!.GetAsync(url);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private sealed record QueueTestMessage(string Value);

    private static async Task<(Guid EntityId, Guid CoMentionEntityId)> SeedDatabaseAsync(
        Aspire.Hosting.DistributedApplication application,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(await application.GetConnectionStringAsync("newsfeed", cancellationToken))
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);

        var firstArticle = new PostgresArticle
        {
            Id = Guid.NewGuid(),
            Title = "Aspire test article",
            Url = "https://www.integration.test/article",
            PublishedTime = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)
        };
        var secondArticle = new PostgresArticle
        {
            Id = Guid.NewGuid(),
            Title = "Second integration article",
            Url = "https://integration.test/article-2",
            PublishedTime = new DateTime(2026, 1, 16, 12, 0, 0, DateTimeKind.Utc)
        };
        var thirdArticle = new PostgresArticle
        {
            Id = Guid.NewGuid(),
            Title = "Third integration article",
            Url = "https://integration.test/article-3",
            PublishedTime = new DateTime(2026, 1, 17, 12, 0, 0, DateTimeKind.Utc)
        };
        var fourthArticle = new PostgresArticle
        {
            Id = Guid.NewGuid(),
            Title = "Publisher two article",
            Url = "https://publisher-two.test/story",
            PublishedTime = new DateTime(2026, 1, 18, 12, 0, 0, DateTimeKind.Utc)
        };
        var entity = new PostgresEntity
        {
            Id = Guid.NewGuid(),
            Name = "Aspire integration entity",
            Type = "ORG"
        };
        var coMentionEntity = new PostgresEntity
        {
            Id = Guid.NewGuid(),
            Name = "Co-mentioned entity",
            Type = "PER"
        };
        db.Articles.AddRange(firstArticle, secondArticle, thirdArticle, fourthArticle);
        db.Entities.AddRange(entity, coMentionEntity);
        db.Mentions.AddRange(
            new PostgresMention { Article = firstArticle, Entity = entity },
            new PostgresMention { Article = firstArticle, Entity = coMentionEntity },
            new PostgresMention { Article = secondArticle, Entity = entity },
            new PostgresMention { Article = secondArticle, Entity = coMentionEntity },
            new PostgresMention { Article = thirdArticle, Entity = entity },
            new PostgresMention { Article = fourthArticle, Entity = entity });

        await db.SaveChangesAsync(cancellationToken);
        return (entity.Id, coMentionEntity.Id);
    }
}