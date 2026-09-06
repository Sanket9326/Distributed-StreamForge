using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class KafkaTopicManager(IOptions<KafkaOptions> options)
{
    private readonly KafkaOptions value = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var admin = CreateClient();
        var timeout = TimeSpan.FromSeconds(value.InitializationTimeoutSeconds);
        var metadata = admin.GetMetadata(timeout);
        var missing = new[] { value.ReactionTopic, value.ViewTopic }
            .Where(name => !metadata.Topics.Any(x => x.Topic == name && x.Error.Code == ErrorCode.NoError))
            .Select(name => new TopicSpecification
            {
                Name = name, NumPartitions = value.PartitionCount, ReplicationFactor = value.ReplicationFactor
            }).ToArray();
        if (missing.Length > 0)
        {
            try
            {
                await admin.CreateTopicsAsync(missing, new CreateTopicsOptions { OperationTimeout = timeout });
            }
            catch (CreateTopicsException exception) when (
                exception.Results.All(x => x.Error.Code == ErrorCode.TopicAlreadyExists)) { }
        }
        await VerifyAvailableAsync(cancellationToken);
    }

    public Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        using var admin = CreateClient();
        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(Math.Min(10, value.InitializationTimeoutSeconds)));
        foreach (var name in new[] { value.ReactionTopic, value.ViewTopic, value.CompletedTopic })
        {
            if (!metadata.Topics.Any(x => x.Topic == name && x.Error.Code == ErrorCode.NoError))
                throw new InvalidOperationException($"Kafka topic '{name}' is unavailable.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private IAdminClient CreateClient() => new AdminClientBuilder(new AdminClientConfig
    {
        BootstrapServers = value.BootstrapServers,
        ClientId = "streamforge-engagement-admin"
    }).Build();
}
