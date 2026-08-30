using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.Worker.Health;

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
            return HealthCheckResult.Unhealthy("Kafka or a required transcoding topic is unavailable.", exception);
        }
    }
}
