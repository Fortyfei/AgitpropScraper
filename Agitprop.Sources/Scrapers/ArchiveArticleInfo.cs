namespace Agitprop.Sources.Scrapers;

/// <summary>
/// Represents information about an article in an archive.
/// </summary>
/// <param name="urlToArticle">The URL of the article.</param>
/// <param name="publishDate">The publication date of the article.</param>
public class ArchiveArticleInfo(string urlToArticle, DateTimeOffset publishDate)
{
    /// <summary>
    /// Gets the URL of the article.
    /// </summary>
    public string UrlToArticle { get; init; } = urlToArticle;

    /// <summary>
    /// Gets the publication date of the article.
    /// </summary>
    public DateTimeOffset PublishDate { get; init; } = publishDate;
}
