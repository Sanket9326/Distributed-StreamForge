using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Verifies the processing topic and creates it only when it does not already exist.
/// </summary>
public sealed class KafkaTopicManager(IOptions<KafkaOptions> options)
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    /// <summary>Ensures the configured topic exists without modifying an existing topic.</summary>
    /// <param name="cancellationToken">Signals that startup initialization should stop.</param>
    public async Task EnsureTopicAsync(CancellationToken cancellationToken)
    {
        using var adminClient = CreateAdminClient();
        var timeout = TimeSpan.FromSeconds(kafkaOptions.InitializationTimeoutSeconds);
        var metadata = adminClient.GetMetadata(timeout);
        var topic = metadata.Topics.SingleOrDefault(candidate =>
            string.Equals(candidate.Topic, kafkaOptions.TopicName, StringComparison.Ordinal));

        if (topic?.Error.Code == ErrorCode.NoError)
        {
            return;
        }

        if (topic is not null && topic.Error.Code != ErrorCode.UnknownTopicOrPart)
        {
            throw new KafkaException(topic.Error);
        }

        try
        {
            await adminClient.CreateTopicsAsync(
                [
                    new TopicSpecification
                    {
                        Name = kafkaOptions.TopicName,
                        NumPartitions = kafkaOptions.PartitionCount,
                        ReplicationFactor = kafkaOptions.ReplicationFactor
                    }
                ],
                new CreateTopicsOptions { OperationTimeout = timeout });
        }
        catch (CreateTopicsException exception) when (
            exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Another instance created the topic between metadata inspection and creation.
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Verifies that Kafka and the configured processing topic are available.</summary>
    /// <param name="cancellationToken">Signals that the availability probe should stop.</param>
    public Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        using var adminClient = CreateAdminClient();
        var timeout = TimeSpan.FromSeconds(Math.Min(10, kafkaOptions.InitializationTimeoutSeconds));
        var metadata = adminClient.GetMetadata(timeout);
        var topic = metadata.Topics.SingleOrDefault(candidate =>
            string.Equals(candidate.Topic, kafkaOptions.TopicName, StringComparison.Ordinal));

        cancellationToken.ThrowIfCancellationRequested();
        if (topic?.Error.Code != ErrorCode.NoError)
        {
            throw new KafkaException(topic?.Error ?? new Error(ErrorCode.UnknownTopicOrPart));
        }

        return Task.CompletedTask;
    }

    private IAdminClient CreateAdminClient() =>
        new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            ClientId = "streamforge-upload-admin"
        }).Build();
}
