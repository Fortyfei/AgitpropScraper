using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class BlikkArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1" };
    protected override List<string> LeadXPaths => new List<string> { "//section[@id='lead']" };

    // The article section also hosts the Indamedia cross-brand teaser slider ("Olvasta már?"),
    // a promotional article link ("Ez is érdekelheti") and a tag-recommendation strip. That copy
    // is not article text; the slider labels unrelated Index articles with an "Index" badge,
    // which made every Blikk article that carries it appear to mention Index.
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//section[@id='article-content']//*[" +
        "self::p or self::h2 or self::h3 or self::h4 or self::blockquote or self::li" +
        "][" +
        "not(ancestor::div[@id='recommendation-slider']) " +
        "and not(ancestor::div[@data-tagrecommendation]) " +
        "and not(ancestor::a[contains(@class, 'mx-auto')])]"
    };
    protected override SourceId SourceSite => BlikkSource.SiteId;
}
