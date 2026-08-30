using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.UnitTests;

public sealed class JobStoreTests
{
    [Theory]
    [InlineData(1, 0, 30)]
    [InlineData(2, 0, 60)]
    [InlineData(3, 0, 120)]
    [InlineData(5, 1, 576)]
    [InlineData(10, 1, 900)]
    public void CalculateRetryDelay_UsesExponentialDelayWithBoundedJitter(
        int attempt,
        double jitter,
        double expectedSeconds)
    {
        var delay = JobStore.CalculateRetryDelay(attempt, 30, 900, jitter);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 5);
    }
}
