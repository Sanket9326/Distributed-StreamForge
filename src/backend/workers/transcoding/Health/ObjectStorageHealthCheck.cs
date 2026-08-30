using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.Worker.Health;

public sealed class ObjectStorageHealthCheck(IObjectStorage objectStorage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await objectStorage.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MinIO rendition storage is unavailable.", exception);
        }
    }
}
