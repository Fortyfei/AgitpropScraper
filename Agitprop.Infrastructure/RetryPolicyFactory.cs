using Polly;

namespace Agitprop.Infrastructure;

public static class RetryPolicyFactory
{
    public static AsyncPolicy CreateExceptionPolicy(
        int retryCount,
        Action<Exception, int> onRetry)
    {
        return Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount,
                GetDelay,
                (exception, _, attempt, _) => onRetry(exception, attempt));
    }

    public static AsyncPolicy<TResult> CreateResultPolicy<TResult>(
        int retryCount,
        Func<TResult, bool> shouldRetry,
        Action<DelegateResult<TResult>, int> onRetry)
    {
        return Policy
            .Handle<Exception>()
            .OrResult(shouldRetry)
            .WaitAndRetryAsync(
                retryCount,
                GetDelay,
                (outcome, _, attempt, _) => onRetry(outcome, attempt));
    }

    private static TimeSpan GetDelay(int attempt) =>
        TimeSpan.FromSeconds(0.5 * attempt);
}
