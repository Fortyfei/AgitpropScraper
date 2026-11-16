using System.Diagnostics;

namespace Agitprop.Web.Diagnostics;

/// <summary>
/// Groups every query a single user interaction triggers under one span.
/// The span continues the trace that opened the Blazor circuit, so a whole browser session —
/// from the first page load to the last click — is exported as a single trace.
/// </summary>
public sealed class UserJourney : IDisposable
{
    public const string SourceName = "Agitprop.Web.Journey";

    private static readonly ActivitySource ActivitySource = new(SourceName);

    private readonly Activity? _activity;

    private UserJourney(Activity? activity) => _activity = activity;

    /// <summary>
    /// Starts the span for an interaction. With a session origin the span becomes a child of the
    /// page load that opened the circuit; without one it starts a new trace.
    /// </summary>
    public static UserJourney Begin(string name, ActivityContext? origin = null)
    {
        var activity = origin is { } parent
            ? ActivitySource.StartActivity(name, ActivityKind.Internal, parent)
            : ActivitySource.StartActivity(name, ActivityKind.Server);

        activity?.SetTag("blazor.journey", name);
        return new UserJourney(activity);
    }

    public Activity? Activity => _activity;

    public void Dispose() => _activity?.Dispose();
}
