using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class NegyNegyNegySource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("NegyNegyNegy");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["444.hu", "www.444.hu"];

    public IContentParser CreateContentParser() => new NegynegynegyArticleContentParser();
    public ILinkParser CreateLinkParser() => new NegynegynegyArchiveLinkParser();
    public IPaginator CreatePaginator() => new NegynegynegyArchivePaginator();

    // The archive is infinite-scroll, so it needs a real browser.
    public PageType ArchivePageType => PageType.Dynamic;

    public List<PageAction>? CreateArchiveActions() =>
        [new(PageActionType.Execute, new NegynegynegyArchiveScrollAction())];

    public string? GetArchiveUrl(DateOnly date) =>
        $"https://www.444.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}";
}
