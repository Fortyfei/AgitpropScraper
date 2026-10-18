using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class RipostSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Ripost");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["ripost.hu", "www.ripost.hu"];

    public IContentParser CreateContentParser() => new RipostArticleContentParser();
    public ILinkParser CreateLinkParser() => new RipostArchiveLinkParser();
    public IPaginator CreatePaginator() => new RipostArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) => $"https://ripost.hu/{date:yyyyMM}_sitemap.xml";
}
