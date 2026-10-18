using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class BlikkSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Blikk");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["blikk.hu", "www.blikk.hu"];

    public IContentParser CreateContentParser() => new BlikkArticleContentParser();
    public ILinkParser CreateLinkParser() => new BlikkArchiveLinkParser();
    public IPaginator CreatePaginator() => new BlikkArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://www.blikk.hu/archivum/online?date={date:yyyy-MM-dd}";
}
