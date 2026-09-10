using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class KafkaTopicManager(IOptions<KafkaOptions> options)
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(kafkaOptions.InitializationTimeoutSeconds);
        using var admin = CreateClient();
        var metadata = admin.GetMetadata(timeout);
        var searchTopic = metadata.Topics.SingleOrDefault(topic =>
            string.Equals(topic.Topic, kafkaOptions.SearchIndexTopic, StringComparison.Ordinal));
        if (searchTopic?.Error.Code != ErrorCode.NoError)
        {
            try
            {
                await admin.CreateTopicsAsync([
                    new TopicSpecification
                    {
                        Name = kafkaOptions.SearchIndexTopic,
                        NumPartitions = kafkaOptions.PartitionCount,
                        ReplicationFactor = kafkaOptions.ReplicationFactor
                    }
                ], new CreateTopicsOptions { OperationTimeout = timeout });
            }
            catch (CreateTopicsException exception) when (
                exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
            {
            }
        }

        await VerifyAsync(timeout, cancellationToken);
    }

    public Task VerifyAvailableAsync(CancellationToken cancellationToken) =>
        VerifyAsync(
            TimeSpan.FromSeconds(Math.Min(10, kafkaOptions.InitializationTimeoutSeconds)),
            cancellationToken);

    private Task VerifyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var admin = CreateClient();
        var metadata = admin.GetMetadata(timeout);
        var required = new[]
        {
            kafkaOptions.UploadedTopic,
            kafkaOptions.CompletedTopic,
            kafkaOptions.SearchIndexTopic
        };
        var unavailable = required.FirstOrDefault(name => !metadata.Topics.Any(topic =>
            string.Equals(topic.Topic, name, StringComparison.Ordinal) &&
            topic.Error.Code == ErrorCode.NoError));
        cancellationToken.ThrowIfCancellationRequested();
        if (unavailable is not null)
        {
            throw new InvalidOperationException($"Kafka topic '{unavailable}' is unavailable.");
        }

        return Task.CompletedTask;
    }

    private IAdminClient CreateClient() => new AdminClientBuilder(new AdminClientConfig
    {
        BootstrapServers = kafkaOptions.BootstrapServers,
        ClientId = "streamforge-feed-admin"
    }).Build();
}
