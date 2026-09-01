using Microsoft.EntityFrameworkCore;
using StreamForge.Feed.Api.Data;

namespace StreamForge.Feed.Api.Services;

public sealed class InfrastructureInitializer(
    IDbContextFactory<FeedDbContext> contextFactory,
    IRenditionStorage renditionStorage,
    KafkaTopicManager topicManager,
    StartupGate startupGate,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    private const long MigrationLockId = 8_307_202_608_310_001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing feed infrastructure");
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_lock({MigrationLockId})",
                cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_unlock({MigrationLockId})",
                cancellationToken);
        }

        await renditionStorage.VerifyAvailableAsync(cancellationToken);
        await topicManager.InitializeAsync(cancellationToken);
        startupGate.MarkReady();
        logger.LogInformation("Feed infrastructure is ready");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
