using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class OrigoSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Origo");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["origo.hu", "www.origo.hu"];

    public IContentParser CreateContentParser() => new OrigoArticleContentParser();
    public ILinkParser CreateLinkParser() => new OrigoArchiveLinkParser();
    public IPaginator CreatePaginator() => new OrigoArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://www.origo.hu/hirarchivum/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}";
}
