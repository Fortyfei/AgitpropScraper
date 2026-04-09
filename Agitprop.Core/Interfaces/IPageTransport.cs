using Agitprop.Core;

namespace Agitprop.Core.Interfaces;

/// <summary>
/// Unified page transport abstraction that selects between static and browser loading.
/// </summary>
public interface IPageTransport
{
    Task<PageLoadResult> LoadAsync(string url, PageLoadOptions? options = null, CancellationToken ct = default);
}

public sealed record PageLoadOptions(
    bool RequiresJavaScript = false,
    List<PageAction>? Actions = null,
    bool Headless = true
);

public sealed record PageLoadResult(
    string Content,
    Uri Url,
    string StrategyName,
    TimeSpan Duration
);

public enum TransportMode
{
    RotatingProxy,
    RespectfulDirect
}
