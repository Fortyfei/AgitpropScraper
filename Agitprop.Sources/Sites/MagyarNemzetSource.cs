using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources.Scrapers.ArchiveLinkParsers;
using Agitprop.Sources.Scrapers.ArchivePaginators;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.Sources.Sites;

internal sealed class MagyarNemzetSource : INewsfeedSource
{
    public static readonly SourceId SiteId = new("MagyarNemzet");

    public SourceId Id => SiteId;
    public IReadOnlyCollection<string> Hosts { get; } = ["magyarnemzet.hu", "www.magyarnemzet.hu"];

    public IContentParser CreateContentParser() => new MagyarNemzetArticleContentParser();
    public ILinkParser CreateLinkParser() => new MagyarNemzetArchiveLinkParser();
    public IPaginator CreatePaginator() => new MagyarNemzetArchivePaginator();

    public string? GetArchiveUrl(DateOnly date) => $"https://magyarnemzet.hu/{date:yyyyMM}_sitemap.xml";
}
