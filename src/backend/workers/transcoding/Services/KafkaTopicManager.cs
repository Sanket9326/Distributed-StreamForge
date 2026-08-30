using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Verifies the consume-only input and owns creation of outcome topics.</summary>
public sealed class KafkaTopicManager(IOptions<KafkaOptions> options)
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var adminClient = CreateClient();
        var timeout = TimeSpan.FromSeconds(kafkaOptions.InitializationTimeoutSeconds);
        var metadata = adminClient.GetMetadata(timeout);
        var input = metadata.Topics.SingleOrDefault(topic =>
            string.Equals(topic.Topic, kafkaOptions.InputTopic, StringComparison.Ordinal));
        if (input?.Error.Code != ErrorCode.NoError)
        {
            throw new InvalidOperationException(
                $"Required input topic '{kafkaOptions.InputTopic}' does not exist or is unavailable.");
        }

        var outputTopics = new[]
        {
            kafkaOptions.CompletedTopic,
            kafkaOptions.FailedTopic,
            kafkaOptions.DeadLetterTopic
        };
        var missing = outputTopics.Where(name => !metadata.Topics.Any(topic =>
            string.Equals(topic.Topic, name, StringComparison.Ordinal) && topic.Error.Code == ErrorCode.NoError));
        var specifications = missing.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = kafkaOptions.PartitionCount,
            ReplicationFactor = kafkaOptions.ReplicationFactor
        }).ToArray();
        if (specifications.Length > 0)
        {
            try
            {
                await adminClient.CreateTopicsAsync(
                    specifications,
                    new CreateTopicsOptions { OperationTimeout = timeout });
            }
            catch (CreateTopicsException exception) when (
                exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                // Another replica created every missing topic concurrently.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        using var adminClient = CreateClient();
        var timeout = TimeSpan.FromSeconds(Math.Min(10, kafkaOptions.InitializationTimeoutSeconds));
        var metadata = adminClient.GetMetadata(timeout);
        var required = new[]
        {
            kafkaOptions.InputTopic,
            kafkaOptions.CompletedTopic,
            kafkaOptions.FailedTopic,
            kafkaOptions.DeadLetterTopic
        };
        var unavailable = required.FirstOrDefault(name => !metadata.Topics.Any(topic =>
            string.Equals(topic.Topic, name, StringComparison.Ordinal) && topic.Error.Code == ErrorCode.NoError));
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
        ClientId = "streamforge-transcoding-admin"
    }).Build();
}
