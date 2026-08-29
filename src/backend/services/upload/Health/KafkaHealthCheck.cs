using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.Api.Health;

/// <summary>
/// Reports whether Kafka and the configured processing topic are available.
/// </summary>
public sealed class KafkaHealthCheck(KafkaTopicManager topicManager) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await topicManager.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy("Kafka topic is available.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Kafka is unavailable.", exception);
        }
    }
}
