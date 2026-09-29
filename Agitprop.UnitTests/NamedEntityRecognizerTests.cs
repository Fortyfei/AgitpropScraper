using Agitprop.Scraper.NLPService;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public class NamedEntityRecognizerTests
{
    private static readonly string[] Labels =
    [
        "B-LOC", "B-MISC", "B-ORG", "B-PER",
        "I-LOC", "I-MISC", "I-ORG", "I-PER", "O"
    ];

    [Test]
    public async Task AnalyzeSingleAsync_CombinesSubwordsAndKeepsOriginalSurfaceForms()
    {
        var model = new RecordingNerModel(new Dictionary<int, int>
        {
            [12] = 3,
            [10] = 0,
            [11] = 4,
            [15] = 0
        });
        using var recognizer = CreateRecognizer(model);

        var result = await recognizer.AnalyzeSingleAsync("Kovács Szeged vármegye, Szentendre");

        Assert.That(result.All.Select(entity => (entity.Name, entity.Type)), Is.EqualTo(new[]
        {
            ("Kovács", "PER"),
            ("Szeged vármegye", "LOC"),
            ("Szentendre", "LOC")
        }));
    }

    [Test]
    public async Task AnalyzeSingleAsync_DeduplicatesBySurfaceNameAndTypeInFirstSeenOrder()
    {
        var model = new RecordingNerModel(new Dictionary<int, int>
        {
            [10] = 0,
            [13] = 0
        });
        using var recognizer = CreateRecognizer(model);

        var result = await recognizer.AnalyzeSingleAsync("Szeged, Budapest, Szeged");

        Assert.That(result.All.Select(entity => entity.Name), Is.EqualTo(new[] { "Szeged", "Budapest" }));
        Assert.That(result.All.Select(entity => entity.Type), Is.All.EqualTo("LOC"));
    }

    [Test]
    public async Task AnalyzeBatchAsync_PreservesInputOrderAndEmptyResults()
    {
        var model = new RecordingNerModel(new Dictionary<int, int>
        {
            [12] = 3
        });
        using var recognizer = CreateRecognizer(model);

        var results = await recognizer.AnalyzeBatchAsync(["no entity", "Kovács", ""]);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Length.EqualTo(3));
            Assert.That(results[0].All, Is.Empty);
            Assert.That(results[1].All.Single().Name, Is.EqualTo("Kovács"));
            Assert.That(results[1].All.Single().Type, Is.EqualTo("PER"));
            Assert.That(results[2].All, Is.Empty);
        });
    }

    [Test]
    public async Task AnalyzeSingleAsync_WithEmptyTextDoesNotRunInference()
    {
        var model = new RecordingNerModel(new Dictionary<int, int>());
        using var recognizer = CreateRecognizer(model);

        var result = await recognizer.AnalyzeSingleAsync("");

        Assert.Multiple(() =>
        {
            Assert.That(result.All, Is.Empty);
            Assert.That(model.CallCount, Is.Zero);
        });
    }

    private static NamedEntityRecognizer CreateRecognizer(RecordingNerModel model)
    {
        var tokenizer = new NerTokenizer(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["[UNK]"] = 1,
                ["[CLS]"] = 2,
                ["[SEP]"] = 3,
                ["Kovács"] = 12,
                ["Szeged"] = 10,
                ["vármegye"] = 11,
                ["Budapest"] = 13,
                ["no"] = 14,
                ["entity"] = 17,
                [","] = 18,
                ["Szent"] = 15,
                ["##endre"] = 16
            },
            "[UNK]",
            "##",
            100);
        return new NamedEntityRecognizer(
            tokenizer,
            model,
            Labels,
            NullLogger<NamedEntityRecognizer>.Instance);
    }

    private sealed class RecordingNerModel(IReadOnlyDictionary<int, int> labelByTokenId) : INerModel
    {
        public int CallCount { get; private set; }

        public float[] Predict(long[] inputIds)
        {
            CallCount++;
            var scores = new float[inputIds.Length * Labels.Length];
            for (var tokenIndex = 0; tokenIndex < inputIds.Length; tokenIndex++)
            {
                var labelIndex = labelByTokenId.TryGetValue((int)inputIds[tokenIndex], out var predictedLabel)
                    ? predictedLabel
                    : 8;
                scores[tokenIndex * Labels.Length + labelIndex] = 1;
            }

            return scores;
        }

        public void Dispose()
        {
        }
    }
}
