using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.Api.Health;

public sealed class UploadStorageHealthCheck(UploadStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.VerifyWritableAsync(cancellationToken);
            return HealthCheckResult.Healthy("Upload storage is writable.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("Upload storage is not writable.", exception);
        }
    }
}
