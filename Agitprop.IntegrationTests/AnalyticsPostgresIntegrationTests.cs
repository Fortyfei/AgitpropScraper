using System.Diagnostics;

using Agitprop.Core;
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

    [Test]
    public async Task CreateMentions_KeepsTiszaRiverAndPartySeparate()
    {
        var url = $"https://entity-resolution.test/tisza-homograph-{Guid.NewGuid():N}";
        await using (var db = CreateDbContext())
        {
            var newsfeed = new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance);
            var entities = new NamedEntityCollection
            {
                Entities =
                [
                    new NamedEntity { Name = "Tisza", Type = "LOC" },
                    new NamedEntity { Name = "Tisza Párt", Type = "ORG" }
                ]
            };

            await newsfeed.CreateMentionsAsync(url, CreateArticle("Tisza homograph"), entities);
        }

        await using (var db = CreateDbContext())
        {
            var mentions = await db.Mentions
                .Where(m => m.Article.Url == url)
                .Select(m => new { m.Entity.Name, m.Entity.Type })
                .ToListAsync();

            Assert.Multiple(() =>
            {
                Assert.That(mentions, Has.Count.EqualTo(2));
                Assert.That(mentions.Select(m => m.Type), Is.EquivalentTo(new[] { "LOC", "ORG" }));
                Assert.That(mentions.Single(m => m.Type == "LOC").Name, Is.EqualTo("Tisza"));
                Assert.That(mentions.Single(m => m.Type == "ORG").Name, Is.EqualTo("Tisza Párt"));
            });
        }
    }

    [Test]
    public async Task CreateMentions_FoldsInflectionsIntoOneMention()
    {
        var url = $"https://entity-resolution.test/inflections-{Guid.NewGuid():N}";
        await using (var db = CreateDbContext())
        {
            var newsfeed = new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance);
            var entities = new NamedEntityCollection
            {
                Entities =
                [
                    new NamedEntity { Name = "Magyarországon", Type = "LOC" },
                    new NamedEntity { Name = "Magyarországra", Type = "LOC" },
                    new NamedEntity { Name = "Magyarország", Type = "LOC" }
                ]
            };

            await newsfeed.CreateMentionsAsync(url, CreateArticle("Inflections"), entities);
        }

        await using (var db = CreateDbContext())
        {
            var mentions = await db.Mentions
                .Where(m => m.Article.Url == url)
                .Select(m => new { m.Entity.Name, m.SurfaceForm })
                .ToListAsync();

            Assert.Multiple(() =>
            {
                Assert.That(mentions, Has.Count.EqualTo(1));
                Assert.That(mentions[0].Name, Is.EqualTo("Magyarország"));
                Assert.That(mentions[0].SurfaceForm, Is.EqualTo("Magyarországon"));
            });
        }
    }

    [Test]
    public async Task CreateMentions_IsIdempotent()
    {
        var url = $"https://entity-resolution.test/idempotent-{Guid.NewGuid():N}";
        var entities = new NamedEntityCollection
        {
            Entities = [new NamedEntity { Name = "Ukrajnában", Type = "LOC" }]
        };

        int first;
        int second;
        await using (var db = CreateDbContext())
        {
            first = await new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance)
                .CreateMentionsAsync(url, CreateArticle("Idempotent"), entities);
        }

        await using (var db = CreateDbContext())
        {
            second = await new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance)
                .CreateMentionsAsync(url, CreateArticle("Idempotent"), entities);
        }

        await using (var db = CreateDbContext())
        {
            var mentionCount = await db.Mentions.CountAsync(m => m.Article.Url == url);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.GreaterThan(0));
                Assert.That(second, Is.Zero);
                Assert.That(mentionCount, Is.EqualTo(1));
            });
        }
    }

    [Test]
    public async Task CreateMentions_RejectsSurfaceNoise()
    {
        var url = $"https://entity-resolution.test/noise-{Guid.NewGuid():N}";
        await using (var db = CreateDbContext())
        {
            var entities = new NamedEntityCollection
            {
                Entities =
                [
                    new NamedEntity { Name = "-", Type = "ORG" },
                    new NamedEntity { Name = "50", Type = "MISC" },
                    new NamedEntity { Name = "Kovács Béla", Type = "PER" }
                ]
            };

            await new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance)
                .CreateMentionsAsync(url, CreateArticle("Noise"), entities);
        }

        await using (var db = CreateDbContext())
        {
            var names = await db.Mentions
                .Where(m => m.Article.Url == url)
                .Select(m => m.Entity.Name)
                .ToListAsync();
            var noiseEntities = await db.Entities
                .Where(e => e.NormalizedName == "-" || e.NormalizedName == "50")
                .AnyAsync();

            Assert.Multiple(() =>
            {
                Assert.That(names, Is.EqualTo(new[] { "Kovács Béla" }));
                Assert.That(noiseEntities, Is.False);
            });
        }
    }

    [Test]
    public async Task EntityCatalogSeeder_SeedsCatalogAndIsIdempotent()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        await EntityCatalogSeeder.SeedAsync(db, NullLogger.Instance);
        await EntityCatalogSeeder.SeedAsync(db, NullLogger.Instance);

        var river = await db.Entities.SingleAsync(entity => entity.Name == "Tisza" && entity.Type == "LOC");
        var party = await db.Entities.SingleAsync(entity => entity.Name == "Tisza Párt" && entity.Type == "ORG");
        var tiszaAliases = await db.EntityAliases
            .Where(alias => alias.EntityId == river.Id || alias.EntityId == party.Id)
            .Select(alias => alias.NormalizedAlias)
            .ToListAsync();
        var catalogCount = await db.Entities.CountAsync(entity => entity.Source == EntitySources.Catalog);

        Assert.Multiple(() =>
        {
            Assert.That(river.Id, Is.Not.EqualTo(party.Id));
            Assert.That(river.Source, Is.EqualTo(EntitySources.Catalog));
            Assert.That(tiszaAliases, Does.Contain("tisza"));
            Assert.That(
                catalogCount,
                Is.EqualTo(EntityCatalog.All.Count),
                "the seeder must not insert duplicates on a second run");
        });
    }

    [Test]
    public async Task CreateMentions_AmbiguousHomographIsNotGuessed()
    {
        var url = $"https://entity-resolution.test/ambiguous-{Guid.NewGuid():N}";
        await using (var db = CreateDbContext())
        {
            var entities = new NamedEntityCollection
            {
                Entities = [new NamedEntity { Name = "Tisza", Type = "MISC" }]
            };

            await new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance)
                .CreateMentionsAsync(url, CreateArticle("Ambiguous"), entities);
        }

        await using (var db = CreateDbContext())
        {
            var mentions = await db.Mentions.CountAsync(m => m.Article.Url == url);
            var bogusEntity = await db.Entities.AnyAsync(e => e.NormalizedName == "tisza" && e.Type == "MISC");

            Assert.Multiple(() =>
            {
                Assert.That(mentions, Is.Zero, "an ambiguous surface form must not be attached to a guess");
                Assert.That(bogusEntity, Is.False, "an ambiguous surface form must not create a third entity");
            });
        }
    }

    [Test]
    public async Task CreateMentions_TwiceOnSameContextStaysConsistent()
    {
        var url = $"https://entity-resolution.test/repeat-{Guid.NewGuid():N}";
        var entities = new NamedEntityCollection
        {
            Entities = [new NamedEntity { Name = "Magyarországra", Type = "LOC" }]
        };

        await using var db = CreateDbContext();
        var newsfeed = new NewsfeedDB(db, NullLogger<NewsfeedDB>.Instance);
        var first = await newsfeed.CreateMentionsAsync(url, CreateArticle("Repeat"), entities);
        var second = await newsfeed.CreateMentionsAsync(url, CreateArticle("Repeat"), entities);
        var mentionCount = await db.Mentions.CountAsync(m => m.Article.Url == url);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.Zero, "the second call must be a no-op, not a duplicate-article skip");
            Assert.That(mentionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task EntityCatalogSeeder_FoldsObservedShadowIntoCatalogEntity()
    {
        var hungary = EntityCatalog.All.Single(entity => entity.Name == "Magyarország" && entity.Type == "LOC").Id;
        var shadowId = Guid.NewGuid();
        var bothUrl = $"https://entity-resolution.test/fold-both-{Guid.NewGuid():N}";
        var shadowOnlyUrl = $"https://entity-resolution.test/fold-shadow-{Guid.NewGuid():N}";

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            await EntityCatalogSeeder.SeedAsync(db, NullLogger.Instance);

            var bothArticle = new PostgresArticle
            {
                Id = Guid.NewGuid(),
                Title = "Both",
                Url = bothUrl,
                PublishedTime = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc)
            };
            var shadowOnlyArticle = new PostgresArticle
            {
                Id = Guid.NewGuid(),
                Title = "Shadow only",
                Url = shadowOnlyUrl,
                PublishedTime = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc)
            };
            var shadow = new PostgresEntity
            {
                Id = shadowId,
                Name = "Magyarországon",
                NormalizedName = "magyarországon",
                Type = "LOC",
                Source = EntitySources.Observed
            };

            db.Articles.AddRange(bothArticle, shadowOnlyArticle);
            db.Entities.Add(shadow);
            db.Mentions.AddRange(
                new PostgresMention { Article = bothArticle, EntityId = hungary, SurfaceForm = "Magyarország", MatchKind = EntityMatchKinds.Exact },
                new PostgresMention { Article = bothArticle, Entity = shadow, SurfaceForm = "Magyarországon" },
                new PostgresMention { Article = shadowOnlyArticle, Entity = shadow, SurfaceForm = "Magyarországon" });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            await EntityCatalogSeeder.SeedAsync(db, NullLogger.Instance);

            var shadowStillThere = await db.Entities.AnyAsync(entity => entity.Id == shadowId);
            var bothMentions = await db.Mentions
                .Where(mention => mention.Article.Url == bothUrl)
                .Select(mention => mention.EntityId)
                .ToListAsync();
            var shadowOnlyMentions = await db.Mentions
                .Where(mention => mention.Article.Url == shadowOnlyUrl)
                .Select(mention => mention.EntityId)
                .ToListAsync();

            Assert.Multiple(() =>
            {
                Assert.That(shadowStillThere, Is.False, "the observed shadow must be folded into the catalog entity");
                Assert.That(bothMentions, Is.EqualTo(new[] { hungary }), "the collision must collapse to one mention");
                Assert.That(shadowOnlyMentions, Is.EqualTo(new[] { hungary }), "the mention must be re-pointed");
            });
        }
    }

    private static ContentParserResult CreateArticle(string title) => new()
    {
        SourceSite = new SourceId("Integration"),
        PublishDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
        Text = title,
        Title = title
    };

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
            NormalizedName = "aspire integration entity",
            Type = "ORG",
            Source = EntitySources.Observed
        };
        var coMentionEntity = new PostgresEntity
        {
            Id = Guid.NewGuid(),
            Name = "Co-mentioned entity",
            NormalizedName = "co-mentioned entity",
            Type = "PER",
            Source = EntitySources.Observed
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
