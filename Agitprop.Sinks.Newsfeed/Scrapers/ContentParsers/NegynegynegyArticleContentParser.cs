using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class NegynegynegyArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "/html/body/div[1]/div[1]/div[4]/div[1]/h1","/html/body/div[1]/div[1]/div[5]/div[1]/h1/span/span" };
    protected override List<string> LeadXPaths => new List<string> { };
    protected override List<string> ArticleXPaths => new List<string> { "/html/body/div[1]/div[1]/div[4]/div[2]/div/div[3]//p","/html/body/div[1]/div[1]/div[5]/div[2]/div/div[3]//p" };
    protected override NewsSites SourceSite => NewsSites.NegyNegyNegy;
}
