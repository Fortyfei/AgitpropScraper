using Agitprop.Core.Models;

namespace Agitprop.UnitTests;

public sealed class AnalyticsServiceTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Test]
    public void GetEntitiesBrowseAsync_RejectsReversedDateRange()
    {
        var repository = new FakeEntityRepository();
        var service = AnalyticsTestHost.Create(repository);

        Assert.Throws<ArgumentException>(() =>
            service.GetEntitiesBrowseAsync(new DateOnly(2026, 2, 1), From, 1, 25, null));

        Assert.That(repository.BrowseCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task GetEntitiesBrowseAsync_ClampsInvalidPaginationAndMapsMentionCounts()
    {
        var repository = new FakeEntityRepository
        {
            BrowseResult =
            (
                [
                    (
                        new Entity { Id = "entity-1", Name = "Integration entity", Type = "ORG" },
                        7
                    )
                ],
                1
            )
        };
        var service = AnalyticsTestHost.Create(repository);

        var result = await service.GetEntitiesBrowseAsync(From, To, page: 0, pageSize: 1000, search: " integration ");

        Assert.Multiple(() =>
        {
            Assert.That(repository.BrowseCalls, Is.EqualTo(1));
            Assert.That(repository.LastPage, Is.EqualTo(1));
            Assert.That(repository.LastPageSize, Is.EqualTo(25));
            Assert.That(repository.LastSearch, Is.EqualTo("integration"));
            Assert.That(result!.TotalCount, Is.EqualTo(1));
            Assert.That(result.Items.Single().MentionCount, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task GetTopMentionedEntitiesAsync_OrdersByMentionsThenName()
    {
        var entities = new FakeEntityRepository
        {
            MentioningArticlesByEntity =
            {
                ["a"] = [new Article { Id = "article-a", Title = "A", Url = "https://a.test" }],
                ["b"] =
                [
                    new Article { Id = "article-b-1", Title = "B1", Url = "https://b.test/1" },
                    new Article { Id = "article-b-2", Title = "B2", Url = "https://b.test/2" }
                ]
            }
        };
        var trending = new FakeTrendingRepository
        {
            Entities =
            [
                new Entity { Id = "a", Name = "Zulu", Type = "PERSON" },
                new Entity { Id = "b", Name = "Alpha", Type = "ORG" }
            ]
        };
        var service = AnalyticsTestHost.Create(entities, trending);

        var result = (await service.GetTopMentionedEntitiesAsync(From, To)).ToList();

        Assert.That(result.Select(entity => entity.Name), Is.EqualTo(new[] { "Alpha", "Zulu" }));
        Assert.That(result.Select(entity => entity.Mentions), Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public async Task GetEntitiesTimelineAsync_ZeroFillsEveryDayInRange()
    {
        var repository = new FakeEntityRepository
        {
            MentioningArticlesByEntity =
            {
                ["a"] =
                [
                    new Article { Id = "1", Title = "A1", Url = "https://a.test/1", PublishedTime = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc) },
                    new Article { Id = "2", Title = "A2", Url = "https://a.test/2", PublishedTime = new DateTime(2026, 1, 2, 18, 0, 0, DateTimeKind.Utc) },
                    new Article { Id = "3", Title = "A3", Url = "https://a.test/3", PublishedTime = new DateTime(2026, 1, 4, 9, 0, 0, DateTimeKind.Utc) }
                ]
            }
        };
        var service = AnalyticsTestHost.Create(repository);

        var timeline = await service.GetEntitiesTimelineAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 4),
            ["a"]);

        Assert.That(timeline["a"].Select(point => (point.Date.Day, point.Count)),
            Is.EqualTo(new[] { (1, 0), (2, 2), (3, 0), (4, 1) }));
    }

    [Test]
    public async Task GetSourcesAsync_MapsPercentagesAndRejectsReversedDateRange()
    {
        var repository = new FakeEntityRepository
        {
            ArticleDomains =
            [
                ("example.com", 3),
                ("news.test", 1)
            ]
        };
        var service = AnalyticsTestHost.Create(repository);

        var sources = (await service.GetSourcesAsync(From, To))!;

        Assert.Multiple(() =>
        {
            Assert.That(sources.Select(source => source.Domain), Is.EqualTo(new[] { "example.com", "news.test" }));
            Assert.That(sources[0].Percent, Is.EqualTo(75));
            Assert.That(repository.ArticleDomainCalls, Is.EqualTo(1));
        });

        Assert.Throws<ArgumentException>(() =>
            service.GetSourcesAsync(new DateOnly(2026, 2, 1), From));
        Assert.That(repository.ArticleDomainCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task GetSourcesAsync_ServesRepeatedReadsFromCache()
    {
        var repository = new FakeEntityRepository { ArticleDomains = [("example.com", 3)] };
        var service = AnalyticsTestHost.Create(repository);

        await service.GetSourcesAsync(From, To);
        await service.GetSourcesAsync(From, To);

        Assert.That(repository.ArticleDomainCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task GetCoMentionsAsync_ReturnsNullWhenTheEntityDoesNotExist()
    {
        var service = AnalyticsTestHost.Create(new FakeEntityRepository { EntityById = null });

        var result = await service.GetCoMentionsAsync(Guid.NewGuid().ToString(), From, To);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetEntityByIdAsync_ReturnsNullWhenTheEntityDoesNotExist()
    {
        var service = AnalyticsTestHost.Create(new FakeEntityRepository { EntityById = null });

        var result = await service.GetEntityByIdAsync(Guid.NewGuid().ToString());

        Assert.That(result, Is.Null);
    }
}
