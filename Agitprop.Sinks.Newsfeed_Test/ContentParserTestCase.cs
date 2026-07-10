using Agitprop.Core;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserTestCase
{
    public string? HtmlPath { get; set; }
    public List<string> HtmlPaths { get; set; } = [];
    public string Url { get; set; } = null!;
    public ContentParserResult ExpectedContent { get; set; } = null!;

    public IEnumerable<string> GetHtmlPaths()
    {
        if (HtmlPaths.Count > 0)
        {
            return HtmlPaths;
        }

        return string.IsNullOrWhiteSpace(HtmlPath)
            ? []
            : [HtmlPath];
    }
}
