using Agitprop.Core;

namespace Agitprop.UnitTests;

public class TelemetryUrlTests
{
    [TestCase("https://example.test/article?token=secret#section", "https://example.test/article")]
    [TestCase("https://example.test/article#section?token=secret", "https://example.test/article")]
    [TestCase("https://user:password@example.test/article?token=secret#section", "https://example.test/article")]
    [TestCase("https://example.test/article", "https://example.test/article")]
    [TestCase("/article?token=secret#section", "/article")]
    [TestCase("article?token=secret#section", "article")]
    [TestCase("//user:password@example.test/article?token=secret", "//example.test/article")]
    [TestCase("http://[invalid?token=secret", "[invalid-url]")]
    [TestCase("", "")]
    public void RedactQueryAndFragment_RemovesQueryAndFragment(string url, string expected)
    {
        Assert.That(TelemetryUrl.RedactQueryAndFragment(url), Is.EqualTo(expected));
    }
}
