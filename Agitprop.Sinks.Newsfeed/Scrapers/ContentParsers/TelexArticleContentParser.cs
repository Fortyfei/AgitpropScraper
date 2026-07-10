using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

// TODO: Parser broken as of 2026-07-10 - site structure may require manual inspection.
internal class TelexArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@name='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//div[@class='title-section__top']" };
    protected override List<string> LeadXPaths => new List<string> { };
    protected override List<string> ArticleXPaths => new List<string> { "//div[contains(@class, 'article-html-content')]" };
    protected override NewsSites SourceSite => NewsSites.Telex;
}
