using System.Diagnostics;

using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Samples new roots by ratio and inherits the decision a parent already made, so a dropped
/// parent never leaves a half-recorded child behind. Roots that anchor a bounded trace - an RSS
/// cycle and a user journey - are always kept: they are low volume and are the entry point used to
/// investigate a problem. Errors inside a sampled trace are kept, because the child inherits the
/// parent decision; a root that fails after it was sampled out cannot be recovered, so the ratio
/// is the knob that trades volume against visibility.
/// </summary>
public sealed class BoundedTraceSampler : Sampler
{
    public const string RssCycleSpanName = "rss.cycle";

    private readonly TraceIdRatioBasedSampler _rootSampler;

    public BoundedTraceSampler(double rootSampleRatio)
    {
        _rootSampler = new TraceIdRatioBasedSampler(rootSampleRatio);
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.ParentContext.IsValid())
        {
            return new SamplingResult(
                samplingParameters.ParentContext.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
                    ? SamplingDecision.RecordAndSample
                    : SamplingDecision.Drop);
        }

        if (samplingParameters.Kind == ActivityKind.Server
            || samplingParameters.Name == RssCycleSpanName)
        {
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }

        return _rootSampler.ShouldSample(samplingParameters);
    }
}
