using Agitprop.Core;

namespace Agitprop.UnitTests;

public class TelemetryUrlTests
{
    [TestCase("https://example.test/article?token=secret#section", "https://example.test/article")]
    [TestCase("https://example.test/article#section?token=secret", "https://example.test/article")]
    [TestCase("https://example.test/article", "https://example.test/article")]
    [TestCase("", "")]
    public void RedactQueryAndFragment_RemovesQueryAndFragment(string url, string expected)
    {
        Assert.That(TelemetryUrl.RedactQueryAndFragment(url), Is.EqualTo(expected));
    }
}
