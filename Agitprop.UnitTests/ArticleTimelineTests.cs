using Agitprop.Core.Models;

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
    public void GetArticleFeedAsync_RejectsInvalidRequestsWithoutQueryingTheRepository()
    {
        var repository = new FakeArticleFeedRepository();
        var service = AnalyticsTestHost.Create(articleFeedRepository: repository);
        var from = new DateOnly(2026, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => service.GetArticleFeedAsync(from.AddDays(1), from, null, null, null, 0, 25));
            Assert.Throws<ArgumentException>(() => service.GetArticleFeedAsync(default, from, null, null, null, 0, 25));
            Assert.Throws<ArgumentException>(() => service.GetArticleFeedAsync(from, from.AddDays(400), null, null, null, 0, 25));
            Assert.Throws<ArgumentException>(() => service.GetArticleFeedAsync(from, from, "not-a-guid", null, null, 0, 25));
            Assert.That(repository.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GetArticleFeedAsync_ClampsPageAndPageSize()
    {
        var repository = new FakeArticleFeedRepository();
        var service = AnalyticsTestHost.Create(articleFeedRepository: repository);
        var from = new DateOnly(2026, 1, 1);

        await service.GetArticleFeedAsync(from, from.AddDays(1), null, null, null, page: -3, pageSize: 5000);

        Assert.Multiple(() =>
        {
            Assert.That(repository.LastPage, Is.EqualTo(0));
            Assert.That(repository.LastPageSize, Is.EqualTo(100));
        });
    }

    [Test]
    public async Task GetDailyArticleCountsAsync_MarksSpikes()
    {
        var service = AnalyticsTestHost.Create(articleFeedRepository: new FakeArticleFeedRepository());
        var from = new DateOnly(2026, 1, 1);

        var days = (await service.GetDailyArticleCountsAsync(from, from.AddDays(9), null, null))!;

        Assert.Multiple(() =>
        {
            Assert.That(days, Has.Count.EqualTo(10));
            Assert.That(days.Where(day => day.IsSpike).Select(day => day.Count), Is.EqualTo(new[] { 20 }));
        });
    }
}
