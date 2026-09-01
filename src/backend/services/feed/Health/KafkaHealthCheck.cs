using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.Api.Health;

public sealed class KafkaHealthCheck(KafkaTopicManager topicManager) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await topicManager.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Kafka or a feed topic is unavailable.", exception);
        }
    }
}
