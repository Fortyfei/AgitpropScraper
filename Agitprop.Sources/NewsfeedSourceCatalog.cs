using Agitprop.Core.Interfaces;

namespace Agitprop.Sources;

public sealed class NewsfeedSourceCatalog : INewsfeedSourceCatalog
{
    private readonly Dictionary<string, INewsfeedSource> _byHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, INewsfeedSource> _byFeedHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, INewsfeedSource> _byId = new(StringComparer.OrdinalIgnoreCase);

    public NewsfeedSourceCatalog(IEnumerable<INewsfeedSource> sources)
    {
        var list = sources.ToList();
        foreach (var source in list)
        {
            Register(_byId, source.Id.Value, source, "source id");
            foreach (var host in source.Hosts)
            {
                Register(_byHost, host, source, "host");
                Register(_byFeedHost, host, source, "host");
            }
            foreach (var host in source.FeedHosts)
                Register(_byFeedHost, host, source, "feed host");
        }
        All = list;
    }

    public IReadOnlyList<INewsfeedSource> All { get; }

    public bool TryGetById(string id, out INewsfeedSource source) => _byId.TryGetValue(id, out source!);

    public bool TryGetByHost(string host, out INewsfeedSource source) => _byHost.TryGetValue(host, out source!);

    public bool TryGetByFeedHost(string host, out INewsfeedSource source) => _byFeedHost.TryGetValue(host, out source!);

    private static void Register(Dictionary<string, INewsfeedSource> map, string key, INewsfeedSource source, string kind)
    {
        if (!map.TryAdd(key, source))
            throw new ArgumentException($"Duplicate {kind} '{key}' registered by sources '{map[key].Id}' and '{source.Id}'.");
    }
}
