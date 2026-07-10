using System.Text.Json;

using Agitprop.Core.Enums;
using NUnit.Framework;

namespace Agitprop.Sinks.Newsfeed_Test;

public static class TestCaseFactory
{
    private static readonly NewsSites[] SupportedSites =
    [
        NewsSites.Alfahir,
        NewsSites.HVG,
        NewsSites.Index,
        NewsSites.MagyarJelen,
        NewsSites.MagyarNemzet,
        NewsSites.Mandiner,
        NewsSites.Merce,
        NewsSites.Metropol,
        NewsSites.Origo,
        NewsSites.PestiSracok,
        NewsSites.Ripost,
        NewsSites.RTL,
        NewsSites.Telex,
        NewsSites.HuszonnegyHu,
        NewsSites.NegyNegyNegy
    ];

    private static readonly (NewsSites Site, int ExpectedCount)[] ArchiveExpectedCounts =
    [
        (NewsSites.HVG, 157),
        (NewsSites.Index, 3437),
        (NewsSites.MagyarJelen, 8),
        (NewsSites.MagyarNemzet, 4062),
        (NewsSites.Mandiner, 3103),
        (NewsSites.Merce, 3),
        (NewsSites.Metropol, 1689),
        (NewsSites.Origo, 100),
        (NewsSites.PestiSracok, 45),
        (NewsSites.Ripost, 1887),
        (NewsSites.RTL, 50),
        (NewsSites.Telex, 85),
        (NewsSites.HuszonnegyHu, 24),
        (NewsSites.NegyNegyNegy, 55)
    ];

    public static IEnumerable<TestCaseData> GetArchiveParserCases()
    {
        foreach (var (site, expectedCount) in ArchiveExpectedCounts)
        {
            yield return new TestCaseData(site, expectedCount)
                .SetName($"ArchiveParser_{site}");
        }
    }

    public static IEnumerable<TestCaseData> GetContentParserOfflineCases()
    {
        foreach (var site in SupportedSites)
        {
            foreach (var testCase in GetContentParserTestCases(site))
            {
                foreach (var htmlPath in testCase.GetHtmlPaths())
                {
                    yield return new TestCaseData(site, testCase, htmlPath)
                        .SetName($"""{site}_{Path.GetFileNameWithoutExtension(htmlPath)}""");
                }
            }
        }
    }

    public static IEnumerable<TestCaseData> GetContentParserOnlineCases()
    {
        foreach (var site in SupportedSites)
        {
            foreach (var testCase in GetContentParserTestCases(site))
            {
                if (!testCase.RunOnline || string.IsNullOrWhiteSpace(testCase.Url))
                {
                    continue;
                }

                yield return new TestCaseData(site, testCase)
                    .SetName($"""{site}_{testCase.ExpectedContent.PublishDate:yyyyMMdd}""");
            }
        }
    }

    internal static IEnumerable<ContentParserTestCase> GetContentParserTestCases(NewsSites site)
    {
        var testCasePath = $"TestData/{site.ToString().ToLower()}/testcases.json";

        var testCases = JsonSerializer.Deserialize<List<ContentParserTestCase>>(File.ReadAllText(testCasePath)) ?? [];
        foreach (var testCase in testCases)
        {
            yield return testCase;
        }
    }

    internal static string GetArchiveParserTestCasePath(NewsSites site)
    {
        return $"TestData/{site.ToString().ToLower()}/archive.html";
    }
}
