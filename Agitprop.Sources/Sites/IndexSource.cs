using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class IndexSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Index");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["index.hu", "www.index.hu"];

    public IContentParser CreateContentParser() => new IndexArticleContentParser();
    public ILinkParser CreateLinkParser() => new IndexArchiveLinkParser();
    public IPaginator CreatePaginator() => new IndexArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) => $"https://index.hu/sitemap/cikkek_{date:yyyyMM}.xml";
}
