using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;

namespace StreamForge.Engagement.Api.Services;

public sealed class InfrastructureInitializer(
    IDbContextFactory<EngagementDbContext> contextFactory,
    IConnectionMultiplexer redis,
    KafkaTopicManager topics,
    StartupGate startupGate,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    private const long MigrationLockId = 8_307_202_609_060_001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({MigrationLockId})", cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({MigrationLockId})", cancellationToken);
        }
        await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
        await topics.InitializeAsync(cancellationToken);
        startupGate.MarkReady();
        logger.LogInformation("Engagement infrastructure is ready");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
