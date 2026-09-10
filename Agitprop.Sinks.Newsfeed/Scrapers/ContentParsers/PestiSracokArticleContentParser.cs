using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class PestiSracokArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']", "//span[contains(@class, 'article-publish-date')]" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[contains(@class, 'story-title entry-title')]", "//h1[contains(@class, 'article-title')]" };
    protected override List<string> LeadXPaths => new List<string> { "//p[contains(@class,'article-lead')]" };
    protected override List<string> ArticleXPaths => new List<string> { "//div[contains(@class, 'wprt-container')]", "//div[contains(@class, 'block-content')]" };
    protected override NewsSites SourceSite => NewsSites.PestiSracok;
}
