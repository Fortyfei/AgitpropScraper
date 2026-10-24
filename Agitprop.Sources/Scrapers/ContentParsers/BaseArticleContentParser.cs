using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Exceptions;
using Agitprop.Core.Interfaces;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal abstract class BaseArticleContentParser : IContentParser
{
    protected abstract List<string> DateXPaths { get; }
    protected abstract List<string> TitleXPaths { get; }
    protected abstract List<string> LeadXPaths { get; }
    protected abstract List<string> ArticleXPaths { get; }
    protected abstract SourceId SourceSite { get; }
    protected virtual bool AllowsLeadOnlyContent => false;

    private HtmlNode? SelectSingleNode(HtmlDocument doc, List<string> xpaths)
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
        var seen = new HashSet<HtmlNode>();
        foreach (var xpath in xpaths)
        {
            var selectedNodes = doc.DocumentNode.SelectNodes(xpath);
            if (selectedNodes != null)
            {
                foreach (var node in selectedNodes)
                {
                    if (seen.Add(node))
                    {
                        nodes.Add(node);
                    }
                }
            }
        }
        return nodes;
    }

    private static string DecodeJsonEscapedValue(string encodedValue)
    {
        var unescaped = Regex.Unescape(encodedValue.Replace("\\/", "/"));
        return WebUtility.HtmlDecode(unescaped);
    }

    private static string ExtractNodeText(HtmlNode node)
    {
        var content = node.Attributes["content"]?.Value;
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content.Trim();
        }

        if (node.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
        {
            var scriptText = node.InnerText ?? string.Empty;
            if (TryGetJsonLdValue(scriptText, "articleBody", out var jsonArticleBody))
            {
                return WebUtility.HtmlDecode(jsonArticleBody).Trim();
            }

            var articleBodyMatch = Regex.Match(scriptText, "\\\"articleBody\\\"\\s*:\\s*\\\"(?<body>.*?)\\\"", RegexOptions.Singleline);
            if (articleBodyMatch.Success)
            {
                return DecodeJsonEscapedValue(articleBodyMatch.Groups["body"].Value).Trim();
            }

            var textValueMatches = Regex.Matches(scriptText, "\\\"key\\\"\\s*:\\s*\\\"text\\\"\\s*,\\s*\\\"value\\\"\\s*:\\s*\\\"(?<body>.*?)\\\"", RegexOptions.Singleline);
            if (textValueMatches.Count > 0)
            {
                var extracted = textValueMatches
                    .Select(match => DecodeJsonEscapedValue(match.Groups["body"].Value))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text =>
                    {
                        var fragment = new HtmlDocument();
                        fragment.LoadHtml(text);
                        return fragment.DocumentNode.InnerText.Trim();
                    })
                    .Where(text => !string.IsNullOrWhiteSpace(text));

                return string.Join(" ", extracted).Trim();
            }
        }

        return node.InnerText?.Trim() ?? string.Empty;
    }

    private static bool TryGetJsonLdValue(string json, string propertyName, out string value)
    {
        value = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryFindJsonLdValue(document.RootElement, propertyName, out value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindJsonLdValue(JsonElement element, string propertyName, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            foreach (var child in element.EnumerateObject())
            {
                if (TryFindJsonLdValue(child.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (TryFindJsonLdValue(child, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
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
                var metadataDateNodes = html.DocumentNode.SelectNodes(
                    "//meta[@property='article:published_time' or @property='og:article:published_time' " +
                    "or @property='og:video:release_date' or @itemprop='datePublished' " +
                    "or @name='date' or @name='publishdate' or @name='pubdate'] | //time[@datetime]");
                if (metadataDateNodes is not null)
                {
                    foreach (var dateNode in metadataDateNodes)
                    {
                        if (TryExtractDate(dateNode, out date))
                        {
                            break;
                        }
                    }
                }
            }

            if (date == DateTime.MinValue)
            {
                var structuredData = html.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
                if (structuredData is not null)
                {
                    foreach (var script in structuredData)
                    {
                        if (TryGetJsonLdValue(script.InnerText ?? string.Empty, "datePublished", out var dateText) &&
                            TryParseDateString(dateText, out date))
                        {
                            break;
                        }
                    }
                }
            }

            if (date == DateTime.MinValue)
            {
                throw new ContentParserException("Date not found or missing content attribute");
            }

            var titleNode = SelectSingleNode(html, TitleXPaths) ??
                SelectSingleNode(html,
                [
                    "//h1",
                    "//meta[@property='og:title']",
                    "//meta[@name='twitter:title']",
                    "//title"
                ]);
            if (titleNode == null)
                throw new ContentParserException("Title not found");

            string titleText = ExtractNodeText(titleNode) + " ";

            var leadNode = SelectSingleNode(html, LeadXPaths);
            string leadText = leadNode != null ? ExtractNodeText(leadNode) + " " : "";

            var articleNodes = SelectMultipleNodes(html, ArticleXPaths);
            if (articleNodes.Count == 0)
            {
                articleNodes = SelectMultipleNodes(html,
                [
                    "//script[contains(., '\"articleBody\"')]",
                    "//*[contains(@class, 'article-content') or contains(@class, 'article-body') " +
                    "or contains(@class, 'article-text') or contains(@class, 'content-body') " +
                    "or contains(@class, 'cikk-torzs')]//*[self::p or self::blockquote]"
                ]);
            }

            if (!articleNodes.Any() &&
                (!AllowsLeadOnlyContent || string.IsNullOrWhiteSpace(leadText)))
                throw new ContentParserException("Article content not found");

            string articleText = string.Join(" ", articleNodes.Select(ExtractNodeText).Where(text => !string.IsNullOrWhiteSpace(text)));

            string concatenatedText = titleText + leadText + articleText;
            concatenatedText = concatenatedText.Replace("Ez a cikk több mint 1 éves.", string.Empty, StringComparison.OrdinalIgnoreCase);
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
