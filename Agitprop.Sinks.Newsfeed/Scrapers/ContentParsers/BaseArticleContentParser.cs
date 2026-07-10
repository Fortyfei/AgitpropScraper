using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Exceptions;
using Agitprop.Core.Interfaces;
using System.Globalization;

using HtmlAgilityPack;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal abstract class BaseArticleContentParser : IContentParser
{
    protected abstract List<string> DateXPaths { get; }
    protected abstract List<string> TitleXPaths { get; }
    protected abstract List<string> LeadXPaths { get; }
    protected abstract List<string> ArticleXPaths { get; }
    protected abstract NewsSites SourceSite { get; }

    private HtmlNode SelectSingleNode(HtmlDocument doc, List<string> xpaths)
    {
        foreach (var xpath in xpaths)
        {
            var node = doc.DocumentNode.SelectSingleNode(xpath);
            if (node != null)
            {
                return node;
            }
        }
        return null;
    }

    private List<HtmlNode> SelectMultipleNodes(HtmlDocument doc, List<string> xpaths)
    {
        var nodes = new List<HtmlNode>();
        foreach (var xpath in xpaths)
        {
            var selectedNodes = doc.DocumentNode.SelectNodes(xpath);
            if (selectedNodes != null)
            {
                nodes.AddRange(selectedNodes);
            }
        }
        return nodes;
    }

    private static bool TryParseDateString(string dateText, out DateTime date)
    {
        var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
        if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, styles, out date))
        {
            return true;
        }

        if (DateTime.TryParse(dateText, new CultureInfo("hu-HU"), styles, out date))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractDate(HtmlNode node, out DateTime date)
    {
        date = DateTime.MinValue;

        var candidateValues = new List<string>();
        var content = node.Attributes["content"]?.Value;
        if (!string.IsNullOrWhiteSpace(content))
        {
            candidateValues.Add(content);
        }

        var dateTime = node.Attributes["datetime"]?.Value;
        if (!string.IsNullOrWhiteSpace(dateTime))
        {
            candidateValues.Add(dateTime);
        }

        var dateTimeCamel = node.Attributes["dateTime"]?.Value;
        if (!string.IsNullOrWhiteSpace(dateTimeCamel))
        {
            candidateValues.Add(dateTimeCamel);
        }

        var innerText = node.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(innerText))
        {
            candidateValues.Add(innerText);
        }

        foreach (var value in candidateValues)
        {
            if (TryParseDateString(value, out date))
            {
                return true;
            }
        }

        return false;
    }

    public Task<ContentParserResult> ParseContentAsync(HtmlDocument html)
    {
        try
        {
            DateTime date = DateTime.MinValue;
            foreach (var xpath in DateXPaths)
            {
                var dateNode = html.DocumentNode.SelectSingleNode(xpath);
                if (dateNode != null && TryExtractDate(dateNode, out date))
                {
                    break;
                }
            }

            if (date == DateTime.MinValue)
            {
                throw new ContentParserException("Date not found or missing content attribute");
            }

            var titleNode = SelectSingleNode(html, TitleXPaths);
            if (titleNode == null)
                throw new ContentParserException("Title not found");

            string titleText = titleNode.InnerText.Trim() + " ";

            var leadNode = SelectSingleNode(html, LeadXPaths);
            string leadText = leadNode != null ? leadNode.InnerText.Trim() + " " : "";

            var articleNodes = SelectMultipleNodes(html, ArticleXPaths);
            if (!articleNodes.Any())
                throw new ContentParserException("Article content not found");

            string articleText = string.Join(" ", articleNodes.Select(node => node.InnerText.Trim()));

            string concatenatedText = titleText + leadText + articleText;
            if (string.IsNullOrWhiteSpace(concatenatedText))
            {
                throw new ContentParserException("Article's content not found");
            }

            return Task.FromResult(new ContentParserResult()
            {
                Title = Helper.CleanUpText(titleText.Trim()),
                PublishDate = date,
                SourceSite = SourceSite,
                Text = Helper.CleanUpText(concatenatedText)
            });
        }
        catch (NullReferenceException ex)
        {
            throw new ContentParserException("Failed to scrape page", ex);
        }
    }

    public Task<ContentParserResult> ParseContentAsync(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParseContentAsync(doc);
    }
}
