using Agitprop.Core.Exceptions;
using Agitprop.Scraper.Consumer.Consumers;
using System.Net;

namespace Agitprop.UnitTests;

public class NewsfeedJobFailureClassifierTests
{
    [TestCaseSource(nameof(PermanentFailures))]
    public void Classify_KnownPermanentFailures_ReturnsPermanent(Exception exception)
    {
        Assert.That(
            NewsfeedJobFailureClassifier.Classify(exception, CancellationToken.None),
            Is.EqualTo(NewsfeedJobFailureKind.Permanent));
    }

    [TestCaseSource(nameof(TransientFailures))]
    public void Classify_TransientTransportFailures_ReturnsTransient(Exception exception)
    {
        Assert.That(
            NewsfeedJobFailureClassifier.Classify(exception, CancellationToken.None),
            Is.EqualTo(NewsfeedJobFailureKind.Transient));
    }

    [Test]
    public void Classify_CancellationRequested_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            NewsfeedJobFailureClassifier.Classify(
                new OperationCanceledException(cancellation.Token),
                cancellation.Token),
            Is.EqualTo(NewsfeedJobFailureKind.Cancelled));
    }

    [Test]
    public void Classify_UnknownFailure_ReturnsUnknown()
    {
        Assert.That(
            NewsfeedJobFailureClassifier.Classify(
                new InvalidOperationException("Unexpected"),
                CancellationToken.None),
            Is.EqualTo(NewsfeedJobFailureKind.Unknown));
    }

    [TestCaseSource(nameof(TransientFailures))]
    public void ShouldRetry_TransientFailure_ReturnsTrue(Exception exception)
    {
        Assert.That(NewsfeedJobFailureClassifier.ShouldRetry(exception), Is.True);
    }

    [TestCaseSource(nameof(PermanentFailures))]
    public void ShouldRetry_PermanentFailure_ReturnsFalse(Exception exception)
    {
        Assert.That(NewsfeedJobFailureClassifier.ShouldRetry(exception), Is.False);
    }

    [Test]
    public void ShouldRetry_CancellationRequested_ReturnsFalse()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            NewsfeedJobFailureClassifier.ShouldRetry(
                new OperationCanceledException(cancellation.Token)),
            Is.False);
    }

    [Test]
    public void ShouldRetry_UnknownFailure_ReturnsFalse()
    {
        Assert.That(
            NewsfeedJobFailureClassifier.ShouldRetry(
                new InvalidOperationException("Unexpected")),
            Is.False);
    }

    private static IEnumerable<Exception> PermanentFailures()
    {
        yield return new ContentParserException("Article content not found");
        yield return new ArgumentException("Unsupported news source");
        yield return new UriFormatException("Invalid URL");
    }

    private static IEnumerable<Exception> TransientFailures()
    {
        yield return new HttpRequestException("Connection failed");
        yield return new TimeoutException("Request timed out");
        yield return new TaskCanceledException("Request timed out");
    }
}
