using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

// TODO: Parser broken as of 2026-07-10 - site structure may require manual inspection.
internal class PestiSracokArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']", "//span[contains(@class, 'article-publish-date')]" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[contains(@class, 'story-title entry-title')]", "//h1[contains(@class, 'article-title')]" };
    protected override List<string> LeadXPaths => new List<string> { };
    protected override List<string> ArticleXPaths => new List<string> { "//div[contains(@class, 'wprt-container')]", "//div[contains(@class, 'block-content')]" };
    protected override NewsSites SourceSite => NewsSites.PestiSracok;
}
