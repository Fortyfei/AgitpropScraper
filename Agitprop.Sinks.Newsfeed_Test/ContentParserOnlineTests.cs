using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserOnlineTests
{
	[TestCase(NewsSites.Alfahir)]
	[TestCase(NewsSites.HVG)]
	[TestCase(NewsSites.Index)]
	//[TestCase("TestData/kurucinfo/testCases.json")]
	[TestCase(NewsSites.MagyarJelen)]
	[TestCase(NewsSites.MagyarNemzet)]
	[TestCase(NewsSites.Mandiner)]
	[TestCase(NewsSites.Merce)]
	[TestCase(NewsSites.Metropol)]
	[TestCase(NewsSites.Origo)]
	[TestCase(NewsSites.PestiSracok)]
	[TestCase(NewsSites.Ripost)]
	[TestCase(NewsSites.RTL)]
	[TestCase(NewsSites.Telex)]
	[TestCase(NewsSites.HuszonnegyHu)]
	[TestCase(NewsSites.NegyNegyNegy)]
	public void ContentParserTest(NewsSites site)
	{
		var scraper = ContentParserFactory.GetContentParser(site);
		using var httpClient = new HttpClient();

		foreach (var testCase in TestCaseFactory.GetContentParserTestCases(site))
		{
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
}
