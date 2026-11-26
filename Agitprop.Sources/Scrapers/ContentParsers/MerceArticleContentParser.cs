using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class MerceArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='entry-title']" };
    protected override List<string> LeadXPaths => new List<string> { };
    // The end-of-article donation box ("Csatlakozom!") is fundraising boilerplate, not article text.
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//div[contains(@class, 'entry-content')]/*[not(contains(@class, 'box-cikkvegi'))]"
    };
    protected override SourceId SourceSite => MerceSource.SiteId;
}
