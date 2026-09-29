using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Agitprop.Scraper.NLPService;

internal sealed class NerTokenizer
{
    private readonly IReadOnlyDictionary<string, int> _vocabulary;
    private readonly string _continuingSubwordPrefix;
    private readonly int _maximumCharactersPerWord;
    private readonly int _classificationTokenId;
    private readonly int _separatorTokenId;
    private readonly int _unknownTokenId;

    internal NerTokenizer(
        IReadOnlyDictionary<string, int> vocabulary,
        string unknownToken,
        string continuingSubwordPrefix,
        int maximumCharactersPerWord)
    {
        _vocabulary = vocabulary;
        _continuingSubwordPrefix = continuingSubwordPrefix;
        _maximumCharactersPerWord = maximumCharactersPerWord;
        _classificationTokenId = GetTokenId("[CLS]");
        _separatorTokenId = GetTokenId("[SEP]");
        _unknownTokenId = GetTokenId(unknownToken);
    }

    internal int ClassificationTokenId => _classificationTokenId;
    internal int SeparatorTokenId => _separatorTokenId;

    internal static NerTokenizer Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var normalizer = root.GetProperty("normalizer");

        if (normalizer.TryGetProperty("lowercase", out var lowerCase) && lowerCase.GetBoolean())
        {
            throw new InvalidDataException("The Hungarian NER tokenizer must preserve letter casing.");
        }

        if (normalizer.TryGetProperty("strip_accents", out var stripAccents)
            && stripAccents.ValueKind == JsonValueKind.True)
        {
            throw new InvalidDataException("The Hungarian NER tokenizer must preserve accents.");
        }

        if (!string.Equals(
                root.GetProperty("pre_tokenizer").GetProperty("type").GetString(),
                "BertPreTokenizer",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The NER tokenizer uses an unsupported pre-tokenizer.");
        }

        var model = root.GetProperty("model");
        if (!string.Equals(model.GetProperty("type").GetString(), "WordPiece", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The NER tokenizer uses an unsupported tokenization model.");
        }

        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in model.GetProperty("vocab").EnumerateObject())
        {
            vocabulary.Add(token.Name, token.Value.GetInt32());
        }

        return new NerTokenizer(
            vocabulary,
            model.GetProperty("unk_token").GetString()
                ?? throw new InvalidDataException("The tokenizer has no unknown token."),
            model.GetProperty("continuing_subword_prefix").GetString()
                ?? throw new InvalidDataException("The tokenizer has no continuation prefix."),
            model.TryGetProperty("max_input_chars_per_word", out var maxCharacters)
                ? maxCharacters.GetInt32()
                : 100);
    }

    internal IReadOnlyList<TokenizedWord> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = new List<TokenizedWord>();
        var wordStart = -1;
        var offset = 0;

        while (offset < text.Length)
        {
            var rune = Rune.GetRuneAt(text, offset);
            var runeLength = rune.Utf16SequenceLength;
            var category = Rune.GetUnicodeCategory(rune);

            if (Rune.IsWhiteSpace(rune) || rune.Value is 0 or 0xfffd || IsControl(category))
            {
                AddWord(text, wordStart, offset, words);
                wordStart = -1;
            }
            else if (IsPunctuation(category))
            {
                AddWord(text, wordStart, offset, words);
                wordStart = -1;
                AddWord(text, offset, offset + runeLength, words);
            }
            else if (wordStart < 0)
            {
                wordStart = offset;
            }

            offset += runeLength;
        }

        AddWord(text, wordStart, text.Length, words);
        return words;
    }

    private void AddWord(string text, int start, int end, List<TokenizedWord> words)
    {
        if (start < 0 || start >= end)
        {
            return;
        }

        var word = text[start..end];
        words.Add(new TokenizedWord(word, start, end, TokenizeWord(word)));
    }

    private int[] TokenizeWord(string word)
    {
        var runes = new List<(int Start, int End)>();
        for (var offset = 0; offset < word.Length;)
        {
            var rune = Rune.GetRuneAt(word, offset);
            var length = rune.Utf16SequenceLength;
            runes.Add((offset, offset + length));
            offset += length;
        }

        if (runes.Count > _maximumCharactersPerWord)
        {
            return [_unknownTokenId];
        }

        var pieces = new List<int>();
        var runeIndex = 0;
        while (runeIndex < runes.Count)
        {
            var pieceEnd = runes.Count;
            var found = false;
            while (pieceEnd > runeIndex)
            {
                var pieceText = word[runes[runeIndex].Start..runes[pieceEnd - 1].End];
                if (runeIndex > 0)
                {
                    pieceText = _continuingSubwordPrefix + pieceText;
                }

                if (_vocabulary.TryGetValue(pieceText, out var tokenId))
                {
                    pieces.Add(tokenId);
                    runeIndex = pieceEnd;
                    found = true;
                    break;
                }

                pieceEnd--;
            }

            if (!found)
            {
                return [_unknownTokenId];
            }
        }

        return pieces.ToArray();
    }

    private int GetTokenId(string token) =>
        _vocabulary.TryGetValue(token, out var tokenId)
            ? tokenId
            : throw new InvalidDataException($"Required tokenizer token '{token}' is missing.");

    private static bool IsControl(UnicodeCategory category) =>
        category is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned;

    private static bool IsPunctuation(UnicodeCategory category) =>
        category is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;
}

internal sealed record TokenizedWord(string Text, int Start, int End, int[] PieceIds);
