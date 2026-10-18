using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class KontrollSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Kontroll");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["kontroll.hu", "www.kontroll.hu"];

    public IContentParser CreateContentParser() => new KontrollArticleContentParser();
    public ILinkParser CreateLinkParser() => new KontrollArchiveLinkParser();
    public IPaginator CreatePaginator() => new KontrollArchivePaginator();

    // The site only publishes one rolling sitemap (about the last week), regardless of the date.
    public string? GetArchiveUrl(DateOnly date) => "https://kontroll.hu/sitemap.xml";
}
