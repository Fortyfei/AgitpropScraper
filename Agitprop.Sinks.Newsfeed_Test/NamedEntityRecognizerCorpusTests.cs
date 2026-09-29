using Agitprop.Scraper.NLPService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.Sinks.Newsfeed_Test;

[Explicit("Downloads the pinned Hungarian NER model assets on first run; requires network access to huggingface.co.")]
public class NamedEntityRecognizerCorpusTests
{
    [Test]
    public async Task AnalyzeBatchAsync_AnalyzesOfflineSinkArticleCorpus()
    {
        var corpus = TestCaseFactory.GetNamedEntityCorpus().ToArray();
        var configuration = new ConfigurationBuilder().Build();
        using var recognizer = new NamedEntityRecognizer(
            NullLogger<NamedEntityRecognizer>.Instance,
            configuration);

        var results = await recognizer.AnalyzeBatchAsync(corpus);
        var entities = results.SelectMany(result => result.All).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(corpus, Is.Not.Empty);
            Assert.That(results, Has.Length.EqualTo(corpus.Length));
            Assert.That(entities, Is.Not.Empty);
            Assert.That(entities.All(entity => !string.IsNullOrWhiteSpace(entity.Name)), Is.True);
            Assert.That(entities.All(entity => entity.Type is "PER" or "LOC" or "ORG" or "MISC"), Is.True);
        });

        TestContext.WriteLine(
            $"Analyzed {corpus.Length} offline articles and recognized {entities.Length} entities.");
    }
}
