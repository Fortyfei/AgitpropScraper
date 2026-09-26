using System.Net;
using System.Text.Json;
using Agitprop.Scraper.NLPService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public class NamedEntityRecognizerTests
{
    [Test]
    public async Task PingAsync_RequestsHealthEndpoint()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("alive")
        }));
        using var client = CreateClient(handler);
        var recognizer = CreateRecognizer(client);

        var result = await recognizer.PingAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Get));
            Assert.That(handler.LastUri!.AbsolutePath, Is.EqualTo("/health"));
            Assert.That(result, Is.EqualTo("alive"));
        });
    }

    [Test]
    public async Task AnalyzeSingleAsync_SendsTextAndDeserializesEntities()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            "[{\"Item1\":\"Budapest\",\"Item2\":\"LOC\"}]")));
        using var client = CreateClient(handler);
        var recognizer = CreateRecognizer(client);

        var result = await recognizer.AnalyzeSingleAsync("News from Budapest");

        using var requestJson = JsonDocument.Parse(handler.LastBody!);
        Assert.Multiple(() =>
        {
            Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.LastUri!.AbsolutePath, Is.EqualTo("/analyzeSingle"));
            Assert.That(requestJson.RootElement.GetProperty("text").GetString(), Is.EqualTo("News from Budapest"));
            Assert.That(result.All, Has.Count.EqualTo(1));
            Assert.That(result.All[0].Name, Is.EqualTo("Budapest"));
            Assert.That(result.All[0].Type, Is.EqualTo("LOC"));
        });
    }

    [Test]
    public async Task AnalyzeBatchAsync_SendsTextsAndPreservesBatchShape()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            "[[],[{\"Item1\":\"Szeged\",\"Item2\":\"LOC\"}]]")));
        using var client = CreateClient(handler);
        var recognizer = CreateRecognizer(client);

        var result = await recognizer.AnalyzeBatchAsync(["No entities", "Szeged"]);

        using var requestJson = JsonDocument.Parse(handler.LastBody!);
        var texts = requestJson.RootElement.GetProperty("texts");
        Assert.Multiple(() =>
        {
            Assert.That(handler.LastUri!.AbsolutePath, Is.EqualTo("/analyzeBatch"));
            Assert.That(texts.GetArrayLength(), Is.EqualTo(2));
            Assert.That(texts[0].GetString(), Is.EqualTo("No entities"));
            Assert.That(texts[1].GetString(), Is.EqualTo("Szeged"));
            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result[0].All, Is.Empty);
            Assert.That(result[1].All[0].Name, Is.EqualTo("Szeged"));
        });
    }

    [Test]
    public async Task AnalyzeSingleAsync_WhenResponseIsUnsuccessful_ThrowsWithStatus()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "upstream unavailable",
            Content = new StringContent("unavailable")
        }));
        using var client = CreateClient(handler);
        var recognizer = CreateRecognizer(client);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => recognizer.AnalyzeSingleAsync("text"));

        Assert.That(exception!.Message, Does.Contain("502"));
        Assert.That(handler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AnalyzeSingleAsync_WhenResponseIsInvalidJson_ThrowsParseError()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("not json")));
        using var client = CreateClient(handler);
        var recognizer = CreateRecognizer(client);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => recognizer.AnalyzeSingleAsync("text"));

        Assert.That(exception!.Message, Is.EqualTo("Failed to parse NLP service response"));
        Assert.That(exception.InnerException, Is.TypeOf<JsonException>());
    }

    [Test]
    public async Task AnalyzeSingleAsync_WhenFirstAttemptFails_RetriesAndReturnsSuccess()
    {
        var handler = new RecordingHandler((_, callCount) => Task.FromResult(
            callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse("[]")));
        using var client = CreateClient(handler);
        var recognizer = CreateRecognizer(client, retryCount: 1);

        var result = await recognizer.AnalyzeSingleAsync("text");

        Assert.That(result.All, Is.Empty);
        Assert.That(handler.CallCount, Is.EqualTo(2));
    }

    private static HttpClient CreateClient(RecordingHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://nlp.test/")
    };

    private static NamedEntityRecognizer CreateRecognizer(HttpClient client, int retryCount = 0)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:NLPService"] = retryCount.ToString()
            })
            .Build();
        return new NamedEntityRecognizer(client, NullLogger<NamedEntityRecognizer>.Instance, configuration);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, CallCount);
        }
    }
}