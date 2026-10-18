using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class MetropolSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Metropol");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["metropol.hu", "www.metropol.hu"];

    public IContentParser CreateContentParser() => new MetropolArticleContentParser();
    public ILinkParser CreateLinkParser() => new MetropolArchiveLinkParser();
    public IPaginator CreatePaginator() => new MetropolArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) => $"https://metropol.hu/{date:yyyyMM}_sitemap.xml";
}
