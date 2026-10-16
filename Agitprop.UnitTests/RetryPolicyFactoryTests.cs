using System.Net;
using Agitprop.Infrastructure;

namespace Agitprop.UnitTests;

public class RetryPolicyFactoryTests
{
    [Test]
    public async Task CreateExceptionPolicy_RetriesConfiguredNumberOfTimes()
    {
        var attempts = 0;
        var retryAttempts = new List<int>();
        var policy = RetryPolicyFactory.CreateExceptionPolicy(
            1,
            (_, attempt) => retryAttempts.Add(attempt));

        await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("Transient failure");
            }

            return Task.CompletedTask;
        });

        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(retryAttempts, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task CreateResultPolicy_RetriesOnlyConfiguredFailureResults()
    {
        var attempts = 0;
        var retryAttempts = new List<int>();
        var responses = new List<HttpResponseMessage>();
        var policy = RetryPolicyFactory.CreateResultPolicy<HttpResponseMessage>(
            1,
            response => !response.IsSuccessStatusCode,
            (_, attempt) => retryAttempts.Add(attempt));

        try
        {
            var response = await policy.ExecuteAsync(() =>
            {
                attempts++;
                var result = new HttpResponseMessage(
                    attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
                responses.Add(result);
                return Task.FromResult(result);
            });

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(retryAttempts, Is.EqualTo(new[] { 1 }));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
