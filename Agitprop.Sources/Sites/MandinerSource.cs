using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class MandinerSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Mandiner");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["mandiner.hu", "www.mandiner.hu"];

    public IContentParser CreateContentParser() => new MandinerArticleContentParser();
    public ILinkParser CreateLinkParser() => new MandinerArchiveLinkParser();
    public IPaginator CreatePaginator() => new MandinerArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) => $"https://mandiner.hu/{date:yyyyMM}_sitemap.xml";
}
