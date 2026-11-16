namespace Agitprop.Web.Diagnostics;

/// <summary>
/// Creates journeys that continue the trace of the page load which opened the circuit.
/// Registered as scoped so each Blazor circuit resolves its own origin.
/// </summary>
public sealed class UserJourneyFactory(CircuitTraceContext traceContext)
{
    public UserJourney Begin(string name) => UserJourney.Begin(name, traceContext.Origin);
}
