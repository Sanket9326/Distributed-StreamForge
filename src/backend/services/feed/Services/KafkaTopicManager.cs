using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class KafkaTopicManager(IOptions<KafkaOptions> options)
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        VerifyAsync(
            TimeSpan.FromSeconds(kafkaOptions.InitializationTimeoutSeconds),
            cancellationToken);

    public Task VerifyAvailableAsync(CancellationToken cancellationToken) =>
        VerifyAsync(
            TimeSpan.FromSeconds(Math.Min(10, kafkaOptions.InitializationTimeoutSeconds)),
            cancellationToken);

    private Task VerifyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            ClientId = "streamforge-feed-admin"
        }).Build();
        var metadata = admin.GetMetadata(timeout);
        var required = new[] { kafkaOptions.UploadedTopic, kafkaOptions.CompletedTopic };
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
}
