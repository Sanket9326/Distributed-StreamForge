using Microsoft.EntityFrameworkCore;
using StreamForge.Transcoding.Worker.Data;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Initializes the service-owned schema, rendition bucket, and outcome topics.</summary>
public sealed class InfrastructureInitializer(
    IDbContextFactory<TranscodingDbContext> contextFactory,
    IObjectStorage objectStorage,
    KafkaTopicManager topicManager,
    StartupGate startupGate,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    private const long MigrationLockId = 8_307_202_608_300_001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing transcoding infrastructure");
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

        await objectStorage.EnsureRenditionsBucketAsync(cancellationToken);
        await topicManager.InitializeAsync(cancellationToken);
        startupGate.MarkReady();
        logger.LogInformation("Transcoding infrastructure is ready");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
