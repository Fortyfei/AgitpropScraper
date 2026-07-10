using Agitprop.Core;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserTestCase
{
    public List<string> HtmlPaths { get; set; } = [];
    public string Url { get; set; } = null!;
    public ContentParserResult ExpectedContent { get; set; } = null!;
    public bool RunOnline { get; set; } = true;

    public IEnumerable<string> GetHtmlPaths()
    {
        return HtmlPaths;
    }
}
