using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Feed.Api.Data;

namespace StreamForge.Feed.Api.Health;

public sealed class DatabaseHealthCheck(IDbContextFactory<FeedDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Feed database is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Feed database check failed.", exception);
        }
    }
}
