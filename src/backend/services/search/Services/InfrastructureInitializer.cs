namespace StreamForge.Search.Api.Services;

public sealed class InfrastructureInitializer(
    IVideoSearchIndex searchIndex,
    KafkaTopicManager topics,
    StartupGate startupGate,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing search infrastructure");
        await searchIndex.EnsureCreatedAsync(cancellationToken);
        await topics.InitializeAsync(cancellationToken);
        startupGate.MarkReady();
        logger.LogInformation("Search infrastructure is ready");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
