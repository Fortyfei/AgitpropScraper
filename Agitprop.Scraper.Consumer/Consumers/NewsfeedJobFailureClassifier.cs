using System;
using System.Threading;
using Agitprop.Core.Exceptions;
using System.Net.Http;

namespace Agitprop.Scraper.Consumer.Consumers;

internal enum NewsfeedJobFailureKind
{
    Permanent,
    Transient,
    Cancelled,
    Unknown
}

internal static class NewsfeedJobFailureClassifier
{
    internal static bool ShouldRetry(Exception exception)
    {
        var cancellationToken = exception is OperationCanceledException cancellationException
            ? cancellationException.CancellationToken
            : CancellationToken.None;

        return Classify(exception, cancellationToken) == NewsfeedJobFailureKind.Transient;
    }

    internal static NewsfeedJobFailureKind Classify(
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? NewsfeedJobFailureKind.Cancelled
                : NewsfeedJobFailureKind.Transient;
        }

        if (exception is ContentParserException or ArgumentException or UriFormatException)
        {
            return NewsfeedJobFailureKind.Permanent;
        }

        if (exception is HttpRequestException or TimeoutException)
        {
            return NewsfeedJobFailureKind.Transient;
        }

        return NewsfeedJobFailureKind.Unknown;
    }
}
