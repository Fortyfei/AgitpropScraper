using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class MagyarJelenArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string>
    {
        "//meta[@property='article:published_time']",
        "//meta[@name='article:published_time']",
        "//div[contains(@class, 'newsPageInfo')]//div[contains(@class, 'newsDate')]",
        "//time[contains(@class, 'post-date')]",
        "//div[contains(@class, 'newsDate')]"
    };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='is-title post-title']", "//h1[contains(@class, 'newsPageTitle')]" };
    protected override List<string> LeadXPaths => new List<string> { };
    protected override List<string> ArticleXPaths => new List<string> { "//div[@class='post-content cf entry-content content-spacious']", "//div[contains(@class, 'textEditorColumn')]" };
    protected override NewsSites SourceSite => NewsSites.MagyarJelen;
}
