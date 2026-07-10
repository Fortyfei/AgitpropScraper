using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserOnlineTests
{
	[TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.GetContentParserOnlineCases))]
	public void ContentParserTest(NewsSites site, ContentParserTestCase testCase)
	{
		var scraper = ContentParserFactory.GetContentParser(site);
		using var httpClient = new HttpClient();

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
