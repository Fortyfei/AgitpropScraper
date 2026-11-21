using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Agitprop.UnitTests;

public class TraceSamplingTests
{
    [Test]
    public void RootSpansWithoutAParent_AreSampledByRatioExceptBoundedTraces()
    {
        var sampler = new BoundedTraceSampler(rootSampleRatio: 0.0);
        var noParent = default(ActivityContext);

        Assert.Multiple(() =>
        {
            Assert.That(Sample(sampler, noParent, "rss.cycle", ActivityKind.Internal),
                Is.EqualTo(SamplingDecision.RecordAndSample));
            Assert.That(Sample(sampler, noParent, "GET /dashboard", ActivityKind.Server),
                Is.EqualTo(SamplingDecision.RecordAndSample));
            Assert.That(Sample(sampler, noParent, "publish", ActivityKind.Producer),
                Is.EqualTo(SamplingDecision.Drop));
        });
    }

    [Test]
    public void SpansWithAParent_InheritTheParentDecision()
    {
        var sampler = new BoundedTraceSampler(rootSampleRatio: 0.0);
        var recorded = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var notRecorded = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None);

        Assert.Multiple(() =>
        {
            Assert.That(Sample(sampler, recorded, "publish", ActivityKind.Producer),
                Is.EqualTo(SamplingDecision.RecordAndSample));
            Assert.That(Sample(sampler, notRecorded, "child", ActivityKind.Internal),
                Is.EqualTo(SamplingDecision.Drop));
        });
    }

    [Test]
    public void RatioOne_KeepsEveryRoot()
    {
        var sampler = new BoundedTraceSampler(rootSampleRatio: 1.0);

        Assert.That(Sample(sampler, default, "publish", ActivityKind.Producer),
            Is.EqualTo(SamplingDecision.RecordAndSample));
    }

    private static SamplingDecision Sample(
        Sampler sampler,
        ActivityContext parent,
        string name,
        ActivityKind kind) =>
        sampler.ShouldSample(new SamplingParameters(
            parent,
            ActivityTraceId.CreateRandom(),
            name,
            kind,
            Array.Empty<KeyValuePair<string, object>>(),
            Array.Empty<ActivityLink>())).Decision;
}
