using System.Diagnostics;

using Agitprop.Web.Diagnostics;

namespace Agitprop.UnitTests;

public sealed class UserJourneyTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    private static ActivityListener CreateListener(List<Activity> stopped) =>
        new()
        {
            ShouldListenTo = source =>
                source.Name is UserJourney.SourceName or "Agitprop.Web.AnalyticsService"
                || source.Name.StartsWith("test.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            }
        };

    [Test]
    public async Task OneJourney_ExportsEveryQueryInTheSameTrace()
    {
        var stopped = new List<Activity>();
        using var listener = CreateListener(stopped);
        ActivitySource.AddActivityListener(listener);

        var service = AnalyticsTestHost.Create(
            new FakeEntityRepository { ArticleDomains = [("example.com", 2)] },
            articleFeedRepository: new FakeArticleFeedRepository());

        using (UserJourney.Begin("test.journey"))
        {
            await service.GetSourcesAsync(From, To);
            await service.GetDailyArticleCountsAsync(From, From.AddDays(9), null, null);
            await service.GetCoMentionsAsync(Guid.NewGuid().ToString(), From, To);
        }

        var journey = stopped.Single(activity => activity.DisplayName == "test.journey");
        var querySpans = stopped.Where(activity => activity.ParentSpanId == journey.SpanId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(stopped.Select(activity => activity.TraceId).Distinct().ToList(), Has.Count.EqualTo(1),
                "every span of one interaction must share a single trace id");
            Assert.That(querySpans.Select(activity => activity.DisplayName),
                Is.EquivalentTo(new[] { "sources.list", "articles.daily", "entities.co_mentions" }));
            Assert.That(stopped.Where(activity => activity.Parent is null), Is.EqualTo(new[] { journey }),
                "the journey must be the only root span");
            Assert.That(journey.GetTagItem("blazor.journey"), Is.EqualTo("test.journey"));
        });
    }

    [Test]
    public async Task SeparateJourneys_ProduceSeparateTraces()
    {
        var stopped = new List<Activity>();
        using var listener = CreateListener(stopped);
        ActivitySource.AddActivityListener(listener);

        var service = AnalyticsTestHost.Create(
            new FakeEntityRepository { ArticleDomains = [("example.com", 2)] },
            articleFeedRepository: new FakeArticleFeedRepository());

        using (UserJourney.Begin("test.first"))
        {
            await service.GetSourcesAsync(From, To);
        }

        using (UserJourney.Begin("test.second"))
        {
            await service.GetArticleFeedAsync(From, To, null, null, null, 0, 25);
        }

        var journeys = stopped.Where(activity => activity.GetTagItem("blazor.journey") is not null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(journeys.Select(activity => activity.DisplayName),
                Is.EquivalentTo(new[] { "test.first", "test.second" }));
            Assert.That(journeys.Select(activity => activity.TraceId).Distinct().ToList(), Has.Count.EqualTo(2),
                "two interactions must not be merged into one trace");
        });
    }

    [Test]
    public async Task QueriesOutsideAJourney_StillWork()
    {
        var service = AnalyticsTestHost.Create(new FakeEntityRepository { ArticleDomains = [("example.com", 1)] });

        var sources = await service.GetSourcesAsync(From, To);

        Assert.That(sources, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task JourneyCreatedFromRestoredOrigin_ContinuesThePageLoadTrace()
    {
        var stopped = new List<Activity>();
        using var listener = CreateListener(stopped);
        ActivitySource.AddActivityListener(listener);

        using var originSource = new ActivitySource("test.page-load");
        using var pageLoad = originSource.StartActivity("GET /");

        var traceContext = new CircuitTraceContext();
        traceContext.Restore(pageLoad!.Id);
        var journeys = new UserJourneyFactory(traceContext);

        var service = AnalyticsTestHost.Create(
            new FakeEntityRepository { ArticleDomains = [("example.com", 1)] });

        using (journeys.Begin("sources.load"))
        {
            await service.GetSourcesAsync(From, To);
        }

        var journey = stopped.Single(activity => activity.DisplayName == "sources.load");
        var query = stopped.Single(activity => activity.DisplayName == "sources.list");

        Assert.Multiple(() =>
        {
            Assert.That(journey.TraceId, Is.EqualTo(pageLoad.TraceId),
                "the session's interactions must continue the page-load trace");
            Assert.That(journey.ParentSpanId, Is.EqualTo(pageLoad.SpanId));
            Assert.That(query.TraceId, Is.EqualTo(pageLoad.TraceId));
        });
    }

    [Test]
    public void CapturedOrigin_IsNotOverwrittenByLaterInteractions()
    {
        var stopped = new List<Activity>();
        using var listener = CreateListener(stopped);
        ActivitySource.AddActivityListener(listener);

        using var originSource = new ActivitySource("test.page-load");
        using var pageLoad = originSource.StartActivity("GET /");

        var traceContext = new CircuitTraceContext();
        traceContext.CaptureFromCurrentActivity();
        var first = traceContext.Origin;

        using var otherSource = new ActivitySource("test.other");
        using var other = otherSource.StartActivity("later");
        traceContext.CaptureFromCurrentActivity();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(traceContext.Origin, Is.EqualTo(first));
            Assert.That(traceContext.Origin!.Value.TraceId, Is.EqualTo(pageLoad!.TraceId));
        });
    }

    [Test]
    public void Restore_IgnoresMalformedTraceParent()
    {
        var traceContext = new CircuitTraceContext();

        traceContext.Restore("not-a-traceparent");
        traceContext.Restore(null);

        Assert.That(traceContext.Origin, Is.Null);
    }
}
