using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Data.Entities;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Publishes outcomes while enforcing that the Upload input topic is consume-only.</summary>
public sealed class KafkaOutboxPublisher : IKafkaOutboxPublisher, IDisposable
{
    private readonly IProducer<string, string> producer;
    private readonly KafkaOptions kafkaOptions;

    public KafkaOutboxPublisher(IOptions<KafkaOptions> options)
    {
        kafkaOptions = options.Value;
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            ClientId = "streamforge-transcoding-outbox",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        EnsureOwnedOutputTopic(message.Topic, kafkaOptions);
        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(message.Id.ToString("D")) },
            { "event-type", Encoding.UTF8.GetBytes(message.Type) },
            { "event-version", Encoding.UTF8.GetBytes(message.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
        };
        await producer.ProduceAsync(
            message.Topic,
            new Message<string, string>
            {
                Key = message.PartitionKey,
                Value = message.Payload,
                Headers = headers
            },
            cancellationToken);
    }

    public static void EnsureOwnedOutputTopic(string topic, KafkaOptions options)
    {
        if (string.Equals(topic, options.InputTopic, StringComparison.Ordinal) ||
            (topic != options.CompletedTopic && topic != options.FailedTopic && topic != options.DeadLetterTopic))
        {
            throw new InvalidOperationException(
                $"Transcoding is not allowed to publish to Kafka topic '{topic}'.");
        }
    }

    public void Dispose() => producer.Dispose();
}
