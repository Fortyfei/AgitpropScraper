using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class HuszonnegyHuSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("HuszonnegyHu");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["24.hu", "www.24.hu"];

    public IContentParser CreateContentParser() => new HuszonnegyArticleContentParser();
    public ILinkParser CreateLinkParser() => new HuszonnegyArchiveLinkParser();
    public IPaginator CreatePaginator() => new HuszonnegyArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://www.24.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}";
}
