using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.Api.Health;

/// <summary>
/// Reports whether MinIO and the configured private source bucket are available.
/// </summary>
public sealed class ObjectStorageHealthCheck(IObjectStorage objectStorage) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await objectStorage.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy("MinIO bucket is available.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MinIO is unavailable.", exception);
        }
    }
}
