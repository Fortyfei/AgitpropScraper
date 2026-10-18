using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

/// <summary>
/// Parses article content from the Alfahir news site.
/// </summary>
internal class AlfahirArticleContentParser : BaseArticleContentParser
{
    /// <summary>
    /// Gets the XPath expressions for extracting the publication date.
    /// </summary>
    protected override List<string> DateXPaths => new List<string> { "//meta[@name='og:article:published_time']/@content" };

    /// <summary>
    /// Gets the XPath expressions for extracting the article title.
    /// </summary>
    protected override List<string> TitleXPaths => new List<string> { "/html/body/main/div/div/article/h1" };

    /// <summary>
    /// Gets the XPath expressions for extracting the article lead (summary).
    /// </summary>
    protected override List<string> LeadXPaths => new List<string> { "/html/body/main/div/div/article/p" };

    /// <summary>
    /// Gets the XPath expressions for extracting the main article content.
    /// </summary>
    protected override List<string> ArticleXPaths => new List<string> { "/html/body/main/div/div/article/div[5]/div/div[1]/*[not(self::div)]" };

    /// <summary>
    /// Gets the source site associated with this parser.
    /// </summary>
    protected override SourceId SourceSite => AlfahirSource.SiteId;
}
