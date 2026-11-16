using System.Diagnostics;

using Agitprop.Data;
using Agitprop.Data.Models;
using Agitprop.Messaging;
using Agitprop.Web.Services;

using Aspire.Hosting.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using OpenTelemetry;
using OpenTelemetry.Trace;

using Projects;

namespace Agitprop.IntegrationTests;

[NonParallelizable]
public class AnalyticsPostgresIntegrationTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly JanuaryEnd = new(2026, 1, 31);

    private IAsyncDisposable? _application;
    private Aspire.Hosting.DistributedApplication? _distributedApplication;
    private string? _connectionString;
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
            .WaitForResourceHealthyAsync("web", timeout.Token)
            .WaitAsync(timeout.Token);

        _connectionString = await application.GetConnectionStringAsync("newsfeed", timeout.Token)
            ?? throw new InvalidOperationException("The newsfeed connection string was not available.");
        (_entityId, _coMentionEntityId) = await SeedDatabaseAsync(application, timeout.Token);
    }

    [OneTimeTearDown]
    public async Task StopApplication()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }

    [Test]
    public async Task BrowseEntitiesWithSearch_QueriesMigratedPostgresDatabase()
    {
        var service = CreateAnalyticsService();

        var result = await service.GetEntitiesBrowseAsync(January, JanuaryEnd, page: 1, pageSize: 25, search: "integration");

        var item = result!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(1));
            Assert.That(item.Id, Is.EqualTo(_entityId.ToString()));
            Assert.That(item.Name, Is.EqualTo("Aspire integration entity"));
            Assert.That(item.MentionCount, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task CoMentions_ReturnSharedArticleCountsFromMigratedPostgresDatabase()
    {
        var service = CreateAnalyticsService();

        var result = await service.GetCoMentionsAsync(_entityId.ToString(), January, JanuaryEnd);

        var related = result!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(related.EntityId, Is.EqualTo(_coMentionEntityId.ToString()));
            Assert.That(related.EntityName, Is.EqualTo("Co-mentioned entity"));
            Assert.That(related.SharedArticleCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SourceEntityCoverage_UsesPublisherArticleTotalsAsDenominator()
    {
        var service = CreateAnalyticsService();

        var sources = (await service.GetSourceEntityCoverageAsync(January, JanuaryEnd))!;

        var integrationSource = sources[0];
        var entities = integrationSource.Entities;
        Assert.Multiple(() =>
        {
            Assert.That(integrationSource.Domain, Is.EqualTo("integration.test"));
            Assert.That(integrationSource.ArticleCount, Is.EqualTo(3));
            Assert.That(entities[0].EntityId, Is.EqualTo(_entityId.ToString()));
            Assert.That(entities[0].ArticleCount, Is.EqualTo(3));
            Assert.That(entities[0].PercentOfSourceArticles, Is.EqualTo(100));
            Assert.That(entities[1].EntityId, Is.EqualTo(_coMentionEntityId.ToString()));
            Assert.That(entities[1].ArticleCount, Is.EqualTo(2));
            Assert.That(entities[1].PercentOfSourceArticles, Is.EqualTo(200.0 / 3).Within(0.01));
            Assert.That(sources[1].Domain, Is.EqualTo("publisher-two.test"));
        });
    }

    [Test]
    public async Task ArticleFeed_FiltersByPublisherEntityAndSearch()
    {
        var service = CreateAnalyticsService();

        var feed = (await service.GetArticleFeedAsync(January, JanuaryEnd, null, null, null, 0, 25))!;
        Assert.Multiple(() =>
        {
            Assert.That(feed.TotalCount, Is.EqualTo(4));
            Assert.That(feed.Items[0].Domain, Is.EqualTo("publisher-two.test"));
            Assert.That(feed.Items[3].Domain, Is.EqualTo("integration.test"));
            Assert.That(feed.Items[3].Entities, Has.Count.EqualTo(2));
        });

        var byDomain = (await service.GetArticleFeedAsync(January, JanuaryEnd, null, "integration.test", null, 0, 2))!;
        Assert.Multiple(() =>
        {
            Assert.That(byDomain.TotalCount, Is.EqualTo(3));
            Assert.That(byDomain.Items, Has.Count.EqualTo(2));
        });

        var byEntity = (await service.GetArticleFeedAsync(
            January, JanuaryEnd, _coMentionEntityId.ToString(), null, null, 0, 25))!;
        Assert.That(byEntity.TotalCount, Is.EqualTo(2));

        var bySearch = (await service.GetArticleFeedAsync(January, JanuaryEnd, null, null, "SECOND", 0, 25))!;
        Assert.That(bySearch.TotalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DailyCounts_ReturnsZeroFilledDays()
    {
        var service = CreateAnalyticsService();

        var daily = (await service.GetDailyArticleCountsAsync(
            new DateOnly(2026, 1, 14),
            new DateOnly(2026, 1, 19),
            null,
            null))!;

        Assert.That(daily.Select(day => day.Count), Is.EqualTo(new[] { 0, 1, 1, 1, 1, 0 }));
    }

    [Test]
    public async Task FirstPublishers_RanksPublishersByEarliestArticle()
    {
        var service = CreateAnalyticsService();

        var publishers = (await service.GetFirstPublishersAsync(_entityId.ToString(), January, JanuaryEnd))!;

        Assert.Multiple(() =>
        {
            Assert.That(publishers[0].Domain, Is.EqualTo("integration.test"));
            Assert.That(publishers[0].ArticleCount, Is.EqualTo(3));
            Assert.That(publishers[1].Domain, Is.EqualTo("publisher-two.test"));
        });
    }

    [Test]
    public async Task HomePage_IsServedByTheWebApplication()
    {
        using var client = _distributedApplication!.CreateHttpClient("web");

        using var response = await client.GetAsync("/");

        Assert.That(response.IsSuccessStatusCode, Is.True, $"GET / returned {response.StatusCode}.");
    }

    [Test]
    public async Task PgmqClient_SendsReadsMovesAndAcknowledgesMessages()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
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
    public async Task PublicationDay_FollowsTheReportingCalendarNotUtc()
    {
        var service = CreateAnalyticsService();
        var articleId = Guid.NewGuid();
        // 2026-02-10 23:30 UTC is 2026-02-11 00:30 in Budapest, so it belongs to the 11th.
        var published = new DateTime(2026, 2, 10, 23, 30, 0, DateTimeKind.Utc);

        await using (var db = CreateDbContext())
        {
            var article = new PostgresArticle
            {
                Id = articleId,
                Title = "Boundary article",
                Url = $"https://bucket-boundary.test/{articleId:N}",
                PublishedTime = published
            };
            db.Articles.Add(article);
            db.Mentions.Add(new PostgresMention { ArticleId = articleId, EntityId = _entityId });
            await db.SaveChangesAsync();
        }

        try
        {
            var counts = (await service.GetDailyArticleCountsAsync(
                new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 11), null, null))!;

            var tenth = (await service.GetArticleFeedAsync(
                new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 10), null, null, null, 0, 25))!;
            var eleventh = (await service.GetArticleFeedAsync(
                new DateOnly(2026, 2, 11), new DateOnly(2026, 2, 11), null, null, null, 0, 25))!;

            var timeline = await service.GetEntitiesTimelineAsync(
                new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 11), [_entityId.ToString()]);

            Assert.Multiple(() =>
            {
                Assert.That(counts.Select(day => day.Count), Is.EqualTo(new[] { 0, 1 }),
                    "the daily buckets must use the reporting calendar");
                Assert.That(tenth.TotalCount, Is.EqualTo(0), "the 10th is the wrong reporting day");
                Assert.That(eleventh.TotalCount, Is.EqualTo(1));
                Assert.That(eleventh.Items.Single().Id, Is.EqualTo(articleId.ToString()));
                Assert.That(timeline[_entityId.ToString()].Select(point => point.Count), Is.EqualTo(new[] { 0, 1 }),
                    "the entity timeline must bucket the same way");
            });
        }
        finally
        {
            await using var db = CreateDbContext();
            var article = await db.Articles.SingleOrDefaultAsync(item => item.Id == articleId);
            if (article is not null)
            {
                db.Articles.Remove(article);
                await db.SaveChangesAsync();
            }
        }
    }

    [Test]
    public async Task PgmqPolling_SuppressesNpgsqlInstrumentationOnlyWithinScope()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
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

    private AnalyticsService CreateAnalyticsService()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:newsfeed"] = _connectionString
        });
        builder.AddNewsfeedRepositories();
        builder.Services.AddMemoryCache();

        var host = builder.Build();

        return new AnalyticsService(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            host.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            NullLogger<AnalyticsService>.Instance);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new AppDbContext(options);
    }

    private sealed class RecordingActivityProcessor(List<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => spans.Add(data);
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
