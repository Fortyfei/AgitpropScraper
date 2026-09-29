using System.Diagnostics;
using System.Text.Json;
using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agitprop.Scraper.NLPService;

public sealed class NamedEntityRecognizer : INamedEntityRecognizer, IDisposable
{
    private const int MaximumModelTokens = 512;
    private const int ChunkOverlapTokens = 64;
    private static readonly ActivitySource ActivitySource = new("Agitprop.NamedEntityRecognizer");
    private static readonly HashSet<string> SupportedEntityTypes = new(StringComparer.Ordinal)
    {
        "PER", "LOC", "ORG", "MISC"
    };

    private readonly NerTokenizer _tokenizer;
    private readonly INerModel _model;
    private readonly IReadOnlyList<string> _labels;
    private readonly ILogger<NamedEntityRecognizer> _logger;

    public NamedEntityRecognizer(
        ILogger<NamedEntityRecognizer> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        var configuredDirectory = configuration["NLP:ModelDirectory"];
        var modelDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "ner-model")
            : Path.GetFullPath(configuredDirectory);
        var modelPath = Path.Combine(modelDirectory, "model.onnx");
        var tokenizerPath = Path.Combine(modelDirectory, "tokenizer.json");
        var configPath = Path.Combine(modelDirectory, "config.json");

        NerModelProvisioner.EnsureAssets(modelDirectory, _logger);

        _tokenizer = NerTokenizer.Load(tokenizerPath);
        _labels = LoadLabels(configPath);
        _model = new OnnxNerModel(modelPath, _labels.Count);

        _logger.LogInformation("Loaded Hungarian NER model from {ModelPath}", modelPath);
    }

    internal NamedEntityRecognizer(
        NerTokenizer tokenizer,
        INerModel model,
        IReadOnlyList<string> labels,
        ILogger<NamedEntityRecognizer> logger)
    {
        _tokenizer = tokenizer;
        _model = model;
        _labels = labels;
        _logger = logger;
    }

    public Task<NamedEntityCollection> AnalyzeSingleAsync(string corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        return Task.Run(() => Analyze(corpus));
    }

    public Task<NamedEntityCollection[]> AnalyzeBatchAsync(string[] corpora)
    {
        ArgumentNullException.ThrowIfNull(corpora);
        return Task.Run(() =>
        {
            var results = new NamedEntityCollection[corpora.Length];
            for (var index = 0; index < corpora.Length; index++)
            {
                results[index] = Analyze(corpora[index]);
            }

            return results;
        });
    }

    public void Dispose() => _model.Dispose();

    private NamedEntityCollection Analyze(string corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        using var activity = ActivitySource.StartActivity("AnalyzeCorpus", ActivityKind.Internal);
        activity?.SetTag("corpus.length", corpus.Length);

        var words = _tokenizer.Tokenize(corpus);
        if (words.Count == 0)
        {
            return new NamedEntityCollection();
        }

        var predictions = PredictWordLabels(words);
        var entities = GetNamedEntities(corpus, words, predictions);

        _logger.LogInformation(
            "Analyzed text (characters={CharacterCount}, entities={EntityCount})",
            corpus.Length,
            entities.Count);
        activity?.SetTag("entities.count", entities.Count);

        return new NamedEntityCollection { Entities = entities };
    }

    private int[] PredictWordLabels(IReadOnlyList<TokenizedWord> words)
    {
        var pieces = new List<ModelToken>();
        for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
        {
            var word = words[wordIndex];
            for (var pieceIndex = 0; pieceIndex < word.PieceIds.Length; pieceIndex++)
            {
                pieces.Add(new ModelToken(word.PieceIds[pieceIndex], wordIndex, pieceIndex == 0));
            }
        }

        var predictions = Enumerable.Repeat(-1, words.Count).ToArray();
        var contextScores = Enumerable.Repeat(-1, words.Count).ToArray();
        var start = 0;

        while (start < pieces.Count)
        {
            var end = Math.Min(start + MaximumModelTokens - 2, pieces.Count);
            while (end < pieces.Count && end > start
                   && pieces[end].WordIndex == pieces[end - 1].WordIndex)
            {
                end--;
            }

            if (end == start)
            {
                throw new InvalidDataException("A word exceeds the NER model's maximum sequence length.");
            }

            var inputIds = new long[end - start + 2];
            inputIds[0] = _tokenizer.ClassificationTokenId;
            for (var tokenIndex = start; tokenIndex < end; tokenIndex++)
            {
                inputIds[tokenIndex - start + 1] = pieces[tokenIndex].TokenId;
            }

            inputIds[^1] = _tokenizer.SeparatorTokenId;
            var logits = _model.Predict(inputIds);
            var sequenceLength = inputIds.Length;
            if (logits.Length != sequenceLength * _labels.Count)
            {
                throw new InvalidDataException(
                    $"NER model returned {logits.Length} scores; expected {sequenceLength * _labels.Count}.");
            }

            for (var tokenIndex = start; tokenIndex < end; tokenIndex++)
            {
                var token = pieces[tokenIndex];
                if (!token.IsFirstPiece)
                {
                    continue;
                }

                var position = tokenIndex - start + 1;
                var labelId = GetBestLabelId(logits, position);
                var contextScore = Math.Min(tokenIndex - start, end - 1 - tokenIndex);
                if (contextScore > contextScores[token.WordIndex])
                {
                    contextScores[token.WordIndex] = contextScore;
                    predictions[token.WordIndex] = labelId;
                }
            }

            if (end == pieces.Count)
            {
                break;
            }

            var nextStart = Math.Max(start + 1, end - ChunkOverlapTokens);
            while (nextStart > start
                   && pieces[nextStart].WordIndex == pieces[nextStart - 1].WordIndex)
            {
                nextStart--;
            }

            start = nextStart > start ? nextStart : end;
        }

        if (predictions.Any(labelId => labelId < 0))
        {
            throw new InvalidDataException("The NER model did not produce a label for every input word.");
        }

        return predictions;
    }

    private int GetBestLabelId(float[] logits, int sequencePosition)
    {
        var scoreOffset = sequencePosition * _labels.Count;
        var bestLabelId = 0;
        if (!float.IsFinite(logits[scoreOffset]))
        {
            throw new InvalidDataException("The NER model returned a non-finite label score.");
        }

        for (var labelId = 1; labelId < _labels.Count; labelId++)
        {
            if (!float.IsFinite(logits[scoreOffset + labelId]))
            {
                throw new InvalidDataException("The NER model returned a non-finite label score.");
            }

            if (logits[scoreOffset + labelId] > logits[scoreOffset + bestLabelId])
            {
                bestLabelId = labelId;
            }
        }

        return bestLabelId;
    }

    private List<NamedEntity> GetNamedEntities(
        string corpus,
        IReadOnlyList<TokenizedWord> words,
        IReadOnlyList<int> predictions)
    {
        var entities = new List<NamedEntity>();
        var seen = new HashSet<(string Name, string Type)>();
        string? currentType = null;
        var entityStart = 0;
        var entityEnd = 0;

        void CompleteEntity()
        {
            if (currentType is null)
            {
                return;
            }

            var name = corpus[entityStart..entityEnd];
            if (seen.Add((name, currentType)))
            {
                entities.Add(new NamedEntity { Name = name, Type = currentType });
            }

            currentType = null;
        }

        for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
        {
            var label = _labels[predictions[wordIndex]];
            if (label == "O")
            {
                CompleteEntity();
                continue;
            }

            if (label.Length < 3 || label[1] != '-'
                || (label[0] != 'B' && label[0] != 'I'))
            {
                throw new InvalidDataException($"The NER model returned an unsupported label '{label}'.");
            }

            var entityType = label[2..];
            if (!SupportedEntityTypes.Contains(entityType))
            {
                throw new InvalidDataException($"The NER model returned an unsupported entity type '{entityType}'.");
            }

            var word = words[wordIndex];
            if (label[0] == 'B' || !string.Equals(currentType, entityType, StringComparison.Ordinal))
            {
                CompleteEntity();
                currentType = entityType;
                entityStart = word.Start;
                entityEnd = word.End;
            }
            else
            {
                entityEnd = word.End;
            }
        }

        CompleteEntity();
        return entities;
    }

    private static IReadOnlyList<string> LoadLabels(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var labelMap = document.RootElement.GetProperty("id2label");
        var labels = labelMap.EnumerateObject()
            .Select(property => (Id: int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture),
                Label: property.Value.GetString()
                    ?? throw new InvalidDataException("The NER model has an empty label.")))
            .OrderBy(pair => pair.Id)
            .ToArray();

        if (labels.Length == 0 || labels.Where((pair, index) => pair.Id != index).Any())
        {
            throw new InvalidDataException("The NER model label IDs must be contiguous and start at zero.");
        }

        return labels.Select(pair => pair.Label).ToArray();
    }

    private sealed record ModelToken(int TokenId, int WordIndex, bool IsFirstPiece);
}
