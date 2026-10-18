using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class AlfahirSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Alfahir");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["alfahir.hu", "www.alfahir.hu"];
    public IReadOnlyCollection<string> FeedHosts { get; } = ["blobs.alfahir.hu"];

    public IContentParser CreateContentParser() => new AlfahirArticleContentParser();
    public ILinkParser CreateLinkParser() => new AlfahirArchiveLinkParser();
    public IPaginator CreatePaginator() => new AlfahirArchivePaginator();
}
