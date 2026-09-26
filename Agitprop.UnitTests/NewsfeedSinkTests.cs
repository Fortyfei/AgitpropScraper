using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public class NewsfeedSinkTests
{
    private const string ArticleUrl = "https://example.test/article";

    [Test]
    public async Task EmitAsync_AnalyzesAndPersistsEachArticleInOrder()
    {
        var calls = new List<string>();
        var recognizer = new RecordingRecognizer(calls);
        var database = new RecordingNewsfeedDb(calls);
        var sink = CreateSink(recognizer, database);
        var articles = new List<ContentParserResult>
        {
            CreateArticle("first"),
            CreateArticle("second")
        };

        await sink.EmitAsync(ArticleUrl, articles);

        Assert.That(calls, Is.EqualTo(new[]
        {
            "analyze:first", "persist:first",
            "analyze:second", "persist:second"
        }));
        Assert.That(database.PersistedArticles, Is.EqualTo(articles));
    }

    [Test]
    public async Task EmitAsync_WithNoArticles_DoesNotCallDependencies()
    {
        var recognizer = new RecordingRecognizer([]);
        var database = new RecordingNewsfeedDb([]);
        var sink = CreateSink(recognizer, database);

        await sink.EmitAsync(ArticleUrl, []);

        Assert.Multiple(() =>
        {
            Assert.That(recognizer.CallCount, Is.Zero);
            Assert.That(database.CallCount, Is.Zero);
        });
    }

    [Test]
    public void EmitAsync_WhenNlpFailsAfterRetries_DoesNotPersistArticle()
    {
        var recognizer = new RecordingRecognizer([])
        {
            AnalyzeException = new InvalidOperationException("NLP unavailable")
        };
        var database = new RecordingNewsfeedDb([]);
        var sink = CreateSink(recognizer, database, retryCount: 0);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.EmitAsync(ArticleUrl, [CreateArticle("article")]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("NLP unavailable"));
            Assert.That(recognizer.CallCount, Is.EqualTo(1));
            Assert.That(database.CallCount, Is.Zero);
        });
    }

    [Test]
    public async Task EmitAsync_WhenNlpInitiallyFails_RetriesBeforePersisting()
    {
        var calls = new List<string>();
        var recognizer = new RecordingRecognizer(calls)
        {
            FailuresRemaining = 1
        };
        var database = new RecordingNewsfeedDb(calls);
        var sink = CreateSink(recognizer, database, retryCount: 1);

        await sink.EmitAsync(ArticleUrl, [CreateArticle("article")]);

        Assert.That(calls, Is.EqualTo(new[]
        {
            "analyze:article", "analyze:article", "persist:article"
        }));
    }

    [Test]
    public void EmitAsync_WhenPersistenceFails_PropagatesFailure()
    {
        var calls = new List<string>();
        var recognizer = new RecordingRecognizer(calls);
        var database = new RecordingNewsfeedDb(calls)
        {
            CreateException = new InvalidOperationException("database unavailable")
        };
        var sink = CreateSink(recognizer, database, retryCount: 0);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.EmitAsync(ArticleUrl, [CreateArticle("article")]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("database unavailable"));
            Assert.That(recognizer.CallCount, Is.EqualTo(1));
            Assert.That(database.CallCount, Is.EqualTo(1));
        });
    }

    private static NewsfeedSink CreateSink(
        INamedEntityRecognizer recognizer,
        INewsfeedDB database,
        int retryCount = 0)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:NewsfeedSink"] = retryCount.ToString()
            })
            .Build();
        return new NewsfeedSink(
            recognizer, database, NullLogger<NewsfeedSink>.Instance, configuration);
    }

    private static ContentParserResult CreateArticle(string text) => new()
    {
        SourceSite = NewsSites.Telex,
        PublishDate = new DateTime(2026, 1, 1),
        Text = text
    };

    private sealed class RecordingRecognizer(List<string> calls) : INamedEntityRecognizer
    {
        public int CallCount { get; private set; }
        public int FailuresRemaining { get; set; }
        public Exception? AnalyzeException { get; set; }

        public Task<NamedEntityCollection> AnalyzeSingleAsync(string corpus)
        {
            CallCount++;
            calls.Add($"analyze:{corpus}");
            if (AnalyzeException is not null)
            {
                throw AnalyzeException;
            }

            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("temporary NLP failure");
            }

            return Task.FromResult(new NamedEntityCollection
            {
                Entities = [new NamedEntity { Name = corpus, Type = "MISC" }]
            });
        }

        public Task<NamedEntityCollection[]> AnalyzeBatchAsync(string[] corpora) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingNewsfeedDb(List<string> calls) : INewsfeedDB
    {
        public int CallCount { get; private set; }
        public Exception? CreateException { get; set; }
        public List<ContentParserResult> PersistedArticles { get; } = [];

        public Task<int> CreateMentionsAsync(
            string url,
            ContentParserResult article,
            NamedEntityCollection entities)
        {
            CallCount++;
            calls.Add($"persist:{article.Text}");
            if (CreateException is not null)
            {
                throw CreateException;
            }

            PersistedArticles.Add(article);
            return Task.FromResult(entities.All.Count);
        }

        public Task<bool> IsUrlAlreadyExists(string url) => Task.FromResult(false);
    }
}