using Agitprop.Scraper.Consumer.Consumers;

namespace Agitprop.UnitTests;

public class NewsfeedJobRetryPolicyTests
{
    [TestCase(1, 1000)]
    [TestCase(2, 2000)]
    [TestCase(3, 5000)]
    public void GetRetryDelay_TransientFailure_ReturnsConfiguredDelay(int readCount, int expectedMilliseconds)
    {
        Assert.That(
            NewsfeedJobRetryPolicy.GetRetryDelay(readCount, NewsfeedJobFailureKind.Transient),
            Is.EqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds)));
    }

    [TestCase(0)]
    [TestCase(4)]
    public void GetRetryDelay_TransientFailureOutsideRetryWindow_ReturnsNull(int readCount)
    {
        Assert.That(
            NewsfeedJobRetryPolicy.GetRetryDelay(readCount, NewsfeedJobFailureKind.Transient),
            Is.Null);
    }

    [TestCase(1, (int)NewsfeedJobFailureKind.Permanent)]
    [TestCase(1, (int)NewsfeedJobFailureKind.Unknown)]
    [TestCase(1, (int)NewsfeedJobFailureKind.Cancelled)]
    public void GetRetryDelay_NonRetryableFailure_ReturnsNull(int readCount, int failureKind)
    {
        Assert.That(NewsfeedJobRetryPolicy.GetRetryDelay(readCount, (NewsfeedJobFailureKind)failureKind), Is.Null);
    }
}
