using System.Diagnostics;

namespace Agitprop.Web.Diagnostics;

/// <summary>
/// Holds the trace context of the page load that opened the Blazor circuit, so every later
/// interaction in the same browser session is exported into that trace instead of starting a new one.
/// Registered as scoped: a circuit owns one instance for its whole lifetime.
/// </summary>
public sealed class CircuitTraceContext
{
    private ActivityContext? _origin;

    public ActivityContext? Origin => _origin;

    /// <summary>Captures the ambient span, called during prerender inside the initial HTTP request.</summary>
    public void CaptureFromCurrentActivity()
    {
        if (_origin is not null)
        {
            return;
        }

        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } current)
        {
            _origin = current.Context;
        }
    }

    /// <summary>Restores the captured span after hydration, from the W3C traceparent value.</summary>
    public void Restore(string? traceParent)
    {
        if (_origin is not null || string.IsNullOrWhiteSpace(traceParent))
        {
            return;
        }

        if (ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var origin))
        {
            _origin = origin;
        }
    }
}
