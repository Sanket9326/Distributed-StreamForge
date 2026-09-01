using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.Api.Health;

public sealed class ObjectStorageHealthCheck(IRenditionStorage renditionStorage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await renditionStorage.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Rendition storage is unavailable.", exception);
        }
    }
}
