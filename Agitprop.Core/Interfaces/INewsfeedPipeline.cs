using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Core.Interfaces;

/// <summary>
/// Resolves a newsfeed job to its source, crawls it, and returns the jobs discovered along the way.
/// Publishing the discovered jobs is the caller's responsibility.
/// </summary>
public interface INewsfeedPipeline
{
    INewsfeedSourceCatalog Sources { get; }

    /// <exception cref="ArgumentException">The job's host or type is not supported.</exception>
    Task<IReadOnlyList<NewsfeedJobDescrpition>> CrawlAsync(
        NewsfeedJobDescrpition job,
        ISink? sinkOverride = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The set of enabled news sources.
/// </summary>
public interface INewsfeedSourceCatalog
{
    IReadOnlyList<INewsfeedSource> All { get; }

    bool TryGetById(string id, out INewsfeedSource source);

    bool TryGetByHost(string host, out INewsfeedSource source);

    /// <summary>Like <see cref="TryGetByHost"/>, but also matches <see cref="INewsfeedSource.FeedHosts"/>.</summary>
    bool TryGetByFeedHost(string host, out INewsfeedSource source);
}
