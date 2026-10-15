using System.Net;
using System.Net.Http.Json;
using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;
using Agitprop.Web.Api.Controllers;
using Agitprop.Web.Api.DTOs.Responses;
using Agitprop.Web.Client.Models;
using Agitprop.Web.Client.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public sealed class WebApiAndClientTests
{
    [Test]
    public void EntitiesController_RejectsReversedDateRange()
    {
        var repository = new FakeEntityRepository();
        var controller = CreateEntitiesController(repository);

        var result = controller.GetEntities(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1));

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(repository.BrowseCalls, Is.EqualTo(0));
    }

    [Test]
    public void EntitiesController_ClampsInvalidPaginationAndMapsMentionCounts()
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
        var controller = CreateEntitiesController(repository);

        var result = controller.GetEntities(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            page: 0,
            pageSize: 1000,
            search: " integration ");

        var response = GetOkValue<EntityBrowseResponse>(result);
        Assert.Multiple(() =>
        {
            Assert.That(repository.BrowseCalls, Is.EqualTo(1));
            Assert.That(repository.LastPage, Is.EqualTo(1));
            Assert.That(repository.LastPageSize, Is.EqualTo(25));
            Assert.That(repository.LastSearch, Is.EqualTo("integration"));
            Assert.That(response.TotalCount, Is.EqualTo(1));
            Assert.That(response.Items.Single().MentionCount, Is.EqualTo(7));
        });
    }

    [Test]
    public void AnalyticsController_OrdersTopEntitiesByMentionsThenName()
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
        var controller = new AnalyticsController(
            NullLogger<AnalyticsController>.Instance,
            entities,
            trending,
            new MemoryCache(new MemoryCacheOptions()));

        var result = controller.GetTopMentionedEntities(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));

        var response = GetOkValue<TopMentionedEntitiesResponse>(result);
        Assert.That(response.Entities.Select(entity => entity.Name), Is.EqualTo(new[] { "Alpha", "Zulu" }));
        Assert.That(response.Entities.Select(entity => entity.MentionCount), Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public void SourcesController_MapsPercentagesAndRejectsReversedDateRange()
    {
        var repository = new FakeEntityRepository
        {
            ArticleDomains =
            [
                ("example.com", 3),
                ("news.test", 1)
            ]
        };
        var controller = new SourcesController(
            NullLogger<SourcesController>.Instance,
            repository,
            new MemoryCache(new MemoryCacheOptions()));

        var result = controller.GetSources(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));

        var response = GetOkValue<SourcesResponse>(result);
        Assert.Multiple(() =>
        {
            Assert.That(response.TotalCount, Is.EqualTo(4));
            Assert.That(response.Sources.Select(source => source.Domain), Is.EqualTo(new[] { "example.com", "news.test" }));
            Assert.That(response.Sources[0].Percent, Is.EqualTo(75));
            Assert.That(repository.ArticleDomainCalls, Is.EqualTo(1));
        });

        var reversed = controller.GetSources(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1));

        Assert.That(reversed.Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(repository.ArticleDomainCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task AnalyticsService_EncodesSearchAndMapsBrowseResponse()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new { id = "entity/1", name = "Entity one", type = "ORG", mentionCount = 4 }
                    },
                    totalCount = 1,
                    page = 2,
                    pageSize = 25
                })
            });
        var service = CreateAnalyticsService(handler);

        var result = await service.GetEntitiesBrowseAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            page: 2,
            pageSize: 25,
            search: "news & politics");

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestUri!.ToString(), Does.Contain("search=news%20%26%20politics"));
            Assert.That(result!.Items.Single().Id, Is.EqualTo("entity/1"));
            Assert.That(result.Items.Single().MentionCount, Is.EqualTo(4));
            Assert.That(result.Page, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AnalyticsService_ReturnsNullWhenHttpRequestFails()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));
        var service = CreateAnalyticsService(handler);

        var result = await service.GetEntityByIdAsync("entity-1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AnalyticsService_MapsSources()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    sources = new[] { new { domain = "example.com", count = 4, percent = 100.0 } },
                    totalCount = 4
                })
            });

        var result = await CreateAnalyticsService(handler).GetSourcesAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));
        var source = result!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestUri!.ToString(), Does.Contain("api/sources?from=2026-01-01&to=2026-01-31"));
            Assert.That(source.Domain, Is.EqualTo("example.com"));
            Assert.That(source.Count, Is.EqualTo(4));
        });
    }

    private static EntitiesController CreateEntitiesController(FakeEntityRepository repository) =>
        new(
            NullLogger<EntitiesController>.Instance,
            repository,
            new FakeTrendingRepository(),
            new MemoryCache(new MemoryCacheOptions()));

    private static AnalyticsService CreateAnalyticsService(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, NullLogger<AnalyticsService>.Instance);

    private static T GetOkValue<T>(ActionResult<T> actionResult)
    {
        var ok = actionResult.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        return (T)ok!.Value!;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class FakeTrendingRepository : ITrendingRepository
    {
        public IEnumerable<Entity> Entities { get; set; } = [];

        public IEnumerable<Entity> GetTrendingEntitiesAsync(DateOnly fromDate, DateOnly toDate, int topN = 10) =>
            Entities;
    }

    private sealed class FakeEntityRepository : IEntityRepository
    {
        public int BrowseCalls { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public string? LastSearch { get; private set; }
        public (IEnumerable<(Entity Entity, int MentionCount)> Items, int TotalCount) BrowseResult { get; set; } = ([], 0);
        public Dictionary<string, IEnumerable<Article>> MentioningArticlesByEntity { get; } = [];
        public IEnumerable<(string Domain, int Count)> ArticleDomains { get; set; } = [];
        public int ArticleDomainCalls { get; private set; }

        public (IEnumerable<(Entity Entity, int MentionCount)> Items, int TotalCount) GetEntitiesWithMentionCountAsync(
            DateOnly from,
            DateOnly to,
            int page,
            int pageSize,
            string? search = null)
        {
            BrowseCalls++;
            LastPage = page;
            LastPageSize = pageSize;
            LastSearch = search;
            return BrowseResult;
        }

        public IDictionary<string, IEnumerable<Article>> GetMentioningArticlesAsync(
            IEnumerable<string> entityIds,
            DateOnly from,
            DateOnly to) =>
            MentioningArticlesByEntity
                .Where(pair => entityIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

        public IEnumerable<Entity> GetEntitiesAsync() => [];
        public IEnumerable<Article> GetMentioningArticlesAsync(string entityId, DateOnly from, DateOnly to) => [];
        public (IEnumerable<Article> Items, int TotalCount) GetMentioningArticlesPaginatedAsync(string entityId, DateOnly from, DateOnly to, int page, int pageSize) => ([], 0);
        public IEnumerable<(string Domain, int Count)> GetMentioningArticlesByDomainAsync(string entityId, DateOnly from, DateOnly to) => [];
        public IEnumerable<(string Domain, int Count)> GetArticleDomainsAsync(DateOnly from, DateOnly to)
        {
            ArticleDomainCalls++;
            return ArticleDomains;
        }
        public Entity? GetEntityByIdAsync(string entityId) => null;
        public IEnumerable<Entity> SearchEntitiesAsync(string query) => [];
        public IEnumerable<Entity> GetEntitiesPaginatedAsync(DateOnly from, DateOnly to, int page, int pageSize) => [];
        public Task<IEnumerable<Entity>> GetAllEntitiesAsync(DateOnly from, DateOnly to) => Task.FromResult<IEnumerable<Entity>>([]);
    }
}
