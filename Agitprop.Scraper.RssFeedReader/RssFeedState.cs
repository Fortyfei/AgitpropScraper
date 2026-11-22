using System.Collections.Concurrent;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// In-memory dedupe state of the RSS feed reader: per feed the HTTP validators and the links that
/// were already published. Links are kept as 64-bit hashes, so the memory a feed can ever take is
/// bounded by <see cref="CapacityPerFeed"/> no matter how long the process runs.
/// </summary>
public sealed class RssFeedState
{
    public const int CapacityPerFeed = 2000;

    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private readonly ConcurrentDictionary<string, FeedCursor> _cursors = new(StringComparer.Ordinal);
    private readonly int _capacityPerFeed;

    public RssFeedState()
        : this(CapacityPerFeed)
    {
    }

    public RssFeedState(int capacityPerFeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityPerFeed);
        _capacityPerFeed = capacityPerFeed;
    }

    public FeedCursor Get(string feedUrl) =>
        _cursors.GetOrAdd(feedUrl, _ => new FeedCursor(_capacityPerFeed));

    /// <summary>
    /// Hash of a normalized link. A 64-bit hash keeps collisions negligible at the bounded capacity
    /// (around 3e-11 for 30000 links) and costs a fraction of retaining the URL string.
    /// </summary>
    public static ulong HashLink(string link)
    {
        var hash = FnvOffsetBasis;
        foreach (var character in link)
        {
            hash ^= character;
            hash *= FnvPrime;
        }

        return hash;
    }
}

/// <summary>
/// Per-feed cursor: the validators of the last successful response and the bounded set of links
/// that were published. Both advance together and only after a successful publish.
/// </summary>
public sealed class FeedCursor
{
    private readonly int _capacity;
    private readonly HashSet<ulong> _published = [];
    private readonly Queue<ulong> _publishOrder = new();
    private readonly object _gate = new();

    internal FeedCursor(int capacity)
    {
        _capacity = capacity;
    }

    public string? EntityTag { get; private set; }

    public string? LastModified { get; private set; }

    public bool HasPublished(string link)
    {
        var hash = RssFeedState.HashLink(link);
        lock (_gate)
        {
            return _published.Contains(hash);
        }
    }

    public void MarkPublished(string link)
    {
        var hash = RssFeedState.HashLink(link);
        lock (_gate)
        {
            if (!_published.Add(hash))
            {
                return;
            }

            _publishOrder.Enqueue(hash);
            while (_publishOrder.Count > _capacity)
            {
                _published.Remove(_publishOrder.Dequeue());
            }
        }
    }

    public void UpdateValidators(string? entityTag, string? lastModified)
    {
        lock (_gate)
        {
            EntityTag = entityTag;
            LastModified = lastModified;
        }
    }
}
