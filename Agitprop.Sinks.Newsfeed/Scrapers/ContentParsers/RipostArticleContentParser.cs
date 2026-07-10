using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class RipostArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='title']","/html/body/app-root/app-base/main/app-article/section/section/div/div/h1" };
    protected override List<string> LeadXPaths => new List<string> { "//div[@class='article-page-lead']", "//p[contains(@class, 'article-lead')]" };
    protected override List<string> ArticleXPaths => new List<string> { "//app-wysiwyg-box" };
    protected override NewsSites SourceSite => NewsSites.Ripost;
}
