using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

// TODO: Parser broken as of 2026-07-10 - site structure may require manual inspection.
internal class HvgArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']", "//time[@datetime]" };
    protected override List<string> TitleXPaths => new List<string> { "//div[@class='article-title article-title']", "//h1[contains(@class, 'title')]" };
    protected override List<string> LeadXPaths => new List<string> { "//p[contains(@class, 'article-lead entry-summary')]", "//p[contains(@class, 'lead')]" };
    protected override List<string> ArticleXPaths => new List<string> { "//div[contains(@class, 'article-content entry-content')]", "//div[@class='article-body']", "//div[contains(@class, 'article-content')]" };
    protected override NewsSites SourceSite => NewsSites.HVG;
}
