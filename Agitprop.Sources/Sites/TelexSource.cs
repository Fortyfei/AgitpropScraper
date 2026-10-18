using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class TelexSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Telex");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["telex.hu", "www.telex.hu"];

    public IContentParser CreateContentParser() => new TelexArticleContentParser();
    public ILinkParser CreateLinkParser() => new TelexArchiveLinkParser();
    public IPaginator CreatePaginator() => new TelexArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://telex.hu/sitemap/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}/news.xml";
}
