using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using StreamForge.Search.Api.Options;

namespace StreamForge.Search.Api.Services;

public sealed class KafkaTopicManager(IOptions<KafkaOptions> options)
{
    private readonly KafkaOptions settings = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var admin = CreateClient();
        var timeout = TimeSpan.FromSeconds(settings.InitializationTimeoutSeconds);
        var metadata = admin.GetMetadata(timeout);
        if (!metadata.Topics.Any(topic =>
            topic.Topic == settings.DeadLetterTopic && topic.Error.Code == ErrorCode.NoError))
        {
            try
            {
                await admin.CreateTopicsAsync([
                    new TopicSpecification
                    {
                        Name = settings.DeadLetterTopic,
                        NumPartitions = settings.PartitionCount,
                        ReplicationFactor = settings.ReplicationFactor
                    }
                ], new CreateTopicsOptions { OperationTimeout = timeout });
            }
            catch (CreateTopicsException exception) when (
                exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
            {
            }
        }

        await VerifyAvailableAsync(cancellationToken);
    }

    public Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        using var admin = CreateClient();
        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(Math.Min(10, settings.InitializationTimeoutSeconds)));
        foreach (var topicName in new[] { settings.InputTopic, settings.DeadLetterTopic })
        {
            if (!metadata.Topics.Any(topic =>
                topic.Topic == topicName && topic.Error.Code == ErrorCode.NoError))
            {
                throw new InvalidOperationException($"Kafka topic '{topicName}' is unavailable.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private IAdminClient CreateClient() => new AdminClientBuilder(new AdminClientConfig
    {
        BootstrapServers = settings.BootstrapServers,
        ClientId = "streamforge-search-admin"
    }).Build();
}
