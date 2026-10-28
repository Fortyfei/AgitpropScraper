using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;
using Agitprop.Web.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public sealed class ArticleTimelineTests
{
    private static IReadOnlyList<DailyArticleCount> Days(params int[] counts) =>
        counts.Select((count, i) => new DailyArticleCount(new DateOnly(2026, 1, 1).AddDays(i), count)).ToList();

    [Test]
    public void SpikeDetector_FlagsOnlyDaysFarAboveTheAverage()
    {
        var marked = SpikeDetector.Mark(Days(2, 3, 2, 3, 2, 3, 2, 3, 2, 20));

        Assert.That(marked.Where(day => day.IsSpike).Select(day => day.Count), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void SpikeDetector_IgnoresShortWindowsAndSmallCounts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpikeDetector.Mark(Days(1, 1, 9)).Any(day => day.IsSpike), Is.False);
            Assert.That(SpikeDetector.Mark(Days(0, 0, 0, 0, 0, 0, 0, 0, 0, 2)).Any(day => day.IsSpike), Is.False);
            Assert.That(SpikeDetector.Mark(Days(4, 4, 4, 4, 4, 4)).Any(day => day.IsSpike), Is.False);
        });
    }

    [Test]
    public void ArticlesController_RejectsInvalidRequestsWithoutQueryingTheRepository()
    {
        var repository = new RecordingFeedRepository();
        var controller = new ArticlesController(NullLogger<ArticlesController>.Instance, repository, new MemoryCache(new MemoryCacheOptions()));
        var from = new DateOnly(2026, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(controller.GetFeed(from.AddDays(1), from, null, null, null).Result, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(controller.GetFeed(default, from, null, null, null).Result, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(controller.GetFeed(from, from.AddDays(400), null, null, null).Result, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(controller.GetFeed(from, from, "not-a-guid", null, null).Result, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(repository.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public void ArticlesController_ClampsPageSizeAndMarksSpikes()
    {
        var repository = new RecordingFeedRepository();
        var controller = new ArticlesController(NullLogger<ArticlesController>.Instance, repository, new MemoryCache(new MemoryCacheOptions()));
        var from = new DateOnly(2026, 1, 1);

        controller.GetFeed(from, from.AddDays(1), null, null, null, page: -3, pageSize: 5000);
        var daily = (OkObjectResult)controller.GetDailyCounts(from, from.AddDays(9), null, null).Result!;

        Assert.Multiple(() =>
        {
            Assert.That(repository.LastPage, Is.EqualTo(0));
            Assert.That(repository.LastPageSize, Is.EqualTo(100));
            Assert.That(daily.Value, Is.Not.Null);
        });
    }

    private sealed class RecordingFeedRepository : IArticleFeedRepository
    {
        public int Calls { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public ArticleFeedPage GetArticleFeed(
            DateOnly from, DateOnly to, string? entityId, string? domain, string? search, int page, int pageSize)
        {
            Calls++;
            LastPage = page;
            LastPageSize = pageSize;
            return new ArticleFeedPage([], 0);
        }

        public IReadOnlyList<DailyArticleCount> GetDailyArticleCounts(DateOnly from, DateOnly to, string? entityId, string? domain)
        {
            Calls++;
            return Days(2, 3, 2, 3, 2, 3, 2, 3, 2, 20);
        }

    }
}
