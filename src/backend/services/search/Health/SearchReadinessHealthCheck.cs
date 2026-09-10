using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.Api.Health;

public sealed class SearchReadinessHealthCheck(
    IVideoSearchIndex searchIndex,
    KafkaTopicManager topics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await searchIndex.VerifyAvailableAsync(cancellationToken);
            await topics.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Search dependencies are unavailable.");
        }
    }
}
