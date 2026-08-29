using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class OutboxPublisherServiceTests
{
    [Theory]
    [InlineData(1, 60, 1)]
    [InlineData(2, 60, 2)]
    [InlineData(6, 60, 32)]
    [InlineData(10, 60, 60)]
    public void CalculateRetryDelay_UsesBoundedExponentialBackoff(
        int attemptCount,
        int maximumSeconds,
        int expectedSeconds)
    {
        var delay = OutboxPublisherService.CalculateRetryDelay(attemptCount, maximumSeconds);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
