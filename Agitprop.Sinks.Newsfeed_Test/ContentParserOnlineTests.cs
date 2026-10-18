using Agitprop.Core;
using System.Net;

namespace Agitprop.Sinks.Newsfeed_Test;
[Explicit("Live-site parser checks are opt-in because source content can change.")]
public class ContentParserOnlineTests
{
	[TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.GetContentParserOnlineCases))]
	public void ContentParserTest(SourceId site, ContentParserTestCase testCase)
	{
		var scraper = TestCaseFactory.GetSource(site).CreateContentParser();
		using var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
		};
		using var httpClient = new HttpClient(handler);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
		httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

		var htmlContent = httpClient.GetStringAsync(testCase.Url).Result;
		var result = scraper.ParseContentAsync(htmlContent).Result;

		Assert.Multiple(() =>
		{
			Assert.That(result.SourceSite, Is.EqualTo(testCase.ExpectedContent.SourceSite));
			Assert.That(result.PublishDate, Is.EqualTo(testCase.ExpectedContent.PublishDate));
			Assert.That(result.Text, Is.EqualTo(testCase.ExpectedContent.Text));
		});
	}
}
