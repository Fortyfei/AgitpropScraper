using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class MerceSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("Merce");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["merce.hu", "www.merce.hu"];

    public IContentParser CreateContentParser() => new MerceArticleContentParser();
    public ILinkParser CreateLinkParser() => new MerceArchiveLinkParser();
    public IPaginator CreatePaginator() => new MerceArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://www.merce.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}";
}
