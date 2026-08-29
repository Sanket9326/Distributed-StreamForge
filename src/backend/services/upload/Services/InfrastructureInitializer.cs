using Microsoft.EntityFrameworkCore;
using StreamForge.Upload.Api.Data;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Blocks application startup until migrations, the MinIO bucket, and the Kafka topic are ready.
/// </summary>
public sealed class InfrastructureInitializer(
    IServiceScopeFactory scopeFactory,
    IObjectStorage objectStorage,
    KafkaTopicManager topicManager,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing upload infrastructure");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
        await objectStorage.EnsureBucketAsync(cancellationToken);
        await topicManager.EnsureTopicAsync(cancellationToken);

        logger.LogInformation("Upload infrastructure is ready");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
