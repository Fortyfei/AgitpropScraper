using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class HvgSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("HVG");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["hvg.hu", "www.hvg.hu"];

    public IContentParser CreateContentParser() => new HvgArticleContentParser();
    public ILinkParser CreateLinkParser() => new HvgArchiveLinkParser();
    public IPaginator CreatePaginator() => new HvgArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://www.hvg.hu/frisshirek/{date.Year:D4}.{date.Month:D2}.{date.Day:D2}";
}
