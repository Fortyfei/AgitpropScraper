using Agitprop.Core;

namespace Agitprop.Sinks.Newsfeed_Test;
internal class ContentParserTestCase
{
    public string HtmlPath { get; set; }
    public string Url { get; set; }
    public ContentParserResult ExpectedContent { get; set; }
}
