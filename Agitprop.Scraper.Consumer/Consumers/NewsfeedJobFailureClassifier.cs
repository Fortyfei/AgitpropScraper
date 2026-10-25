using System;
using System.Collections.Generic;
using System.Data.Common;
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
    private static readonly HashSet<string> TransientDatabaseSqlStates = new(StringComparer.Ordinal)
    {
        "40001",
        "40P01",
        "53300",
        "55P03",
        "57P01",
        "57P02",
        "57P03"
    };

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

        var databaseFailure = ClassifyDatabaseFailure(exception);
        if (databaseFailure.HasValue)
        {
            return databaseFailure.Value;
        }

        return NewsfeedJobFailureKind.Unknown;
    }

    private static NewsfeedJobFailureKind? ClassifyDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not DbException { SqlState: { } sqlState })
            {
                continue;
            }

            if (TransientDatabaseSqlStates.Contains(sqlState) ||
                sqlState.StartsWith("08", StringComparison.Ordinal))
            {
                return NewsfeedJobFailureKind.Transient;
            }

            if (sqlState.StartsWith("23", StringComparison.Ordinal))
            {
                return NewsfeedJobFailureKind.Permanent;
            }
        }

        return null;
    }
}
