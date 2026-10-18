using System.Text.Json;

using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sources;
using NUnit.Framework;

namespace Agitprop.Sinks.Newsfeed_Test;

public static class TestCaseFactory
{
    private static readonly INewsfeedSourceCatalog Catalog = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());

    internal static INewsfeedSource GetSource(SourceId id) =>
        Catalog.TryGetById(id.Value, out var source) ? source : throw new ArgumentException($"Unknown source {id}");

    private static readonly SourceId[] SupportedSites =
    [
        "Alfahir",
        "HVG",
        "Index",
        "MagyarNemzet",
        "Mandiner",
        "Merce",
        "Metropol",
        "Origo",
        "Ripost",
        "RTL",
        "Telex",
        "HuszonnegyHu",
        "NegyNegyNegy",
        "Blikk",
        "Kontroll"
    ];

    private static readonly (SourceId Site, int ExpectedCount)[] ArchiveExpectedCounts =
    [
        ("HVG", 157),
        ("Index", 3437),
        ("MagyarNemzet", 4062),
        ("Mandiner", 3103),
        ("Merce", 3),
        ("Metropol", 1689),
        ("Origo", 100),
        ("Ripost", 1887),
        ("RTL", 50),
        ("Telex", 85),
        ("HuszonnegyHu", 24),
        ("NegyNegyNegy", 55),
        ("Blikk", 30),
        ("Kontroll", 100)
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

    public static IEnumerable<string> GetNamedEntityCorpus()
    {
        foreach (var site in SupportedSites)
        {
            foreach (var testCase in GetContentParserTestCases(site))
            {
                if (!string.IsNullOrWhiteSpace(testCase.ExpectedContent.Text))
                {
                    yield return testCase.ExpectedContent.Text;
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

    internal static IEnumerable<ContentParserTestCase> GetContentParserTestCases(SourceId site)
    {
        var testCasePath = $"TestData/{site.ToString().ToLower()}/testcases.json";

        var testCases = JsonSerializer.Deserialize<List<ContentParserTestCase>>(File.ReadAllText(testCasePath)) ?? [];
        foreach (var testCase in testCases)
        {
            yield return testCase;
        }
    }

    internal static string GetArchiveParserTestCasePath(SourceId site)
    {
        return $"TestData/{site.ToString().ToLower()}/archive.html";
    }
}
