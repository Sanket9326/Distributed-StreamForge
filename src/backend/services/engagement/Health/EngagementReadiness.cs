using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.Api.Health;

public sealed class EngagementReadiness(
    IDbContextFactory<EngagementDbContext> contextFactory,
    IConnectionMultiplexer redis,
    KafkaTopicManager topics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Engagement database unavailable.");
            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            await topics.VerifyAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Engagement dependencies unavailable.");
        }
    }
}
