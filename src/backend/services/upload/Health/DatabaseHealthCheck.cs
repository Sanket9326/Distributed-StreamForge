using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Upload.Api.Data;

namespace StreamForge.Upload.Api.Health;

/// <summary>
/// Reports whether the Upload service can connect to PostgreSQL.
/// </summary>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is available.")
                : HealthCheckResult.Unhealthy("PostgreSQL rejected the health probe.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.", exception);
        }
    }
}
