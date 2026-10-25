using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class IndexArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string>
    {
        "//div[@class='content-title']",
        "//h1",
        "//meta[@property='og:title']"
    };
    protected override List<string> LeadXPaths => new List<string> { "//div[@class='lead']" };
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//div[@class='cikk-torzs']/*[" +
        "not(self::script or self::style) " +
        "and not(contains(@class, 'cikk-bottom')) " +
        "and not(contains(@class, 'social-stripe')) " +
        "and not(contains(@class, 'google-pref-banner')) " +
        "and not(contains(@class, 'microsite')) " +
        "and not(contains(@class, 'pp-list'))]"
    };
    protected override SourceId SourceSite => IndexSource.SiteId;
}
