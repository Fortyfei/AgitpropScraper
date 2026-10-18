using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class RtlSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("RTL");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["rtl.hu", "www.rtl.hu"];
    public IReadOnlyCollection<string> FeedHosts { get; } = ["rss.rtl.hu"];

    public IContentParser CreateContentParser() => new RtlArticleContentParser();
    public ILinkParser CreateLinkParser() => new RtlArchiveLinkParser();
    public IPaginator CreatePaginator() => new RtlArchivePaginator();
}
