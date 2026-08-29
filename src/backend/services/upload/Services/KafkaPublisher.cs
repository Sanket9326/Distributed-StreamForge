using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Upload.Api.Data.Entities;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Publishes outbox payloads with Kafka idempotence and full broker acknowledgement enabled.
/// </summary>
public sealed class KafkaPublisher : IKafkaPublisher, IDisposable
{
    private readonly IProducer<string, string> producer;

    /// <summary>Creates the long-lived Kafka producer used by the outbox publisher.</summary>
    /// <param name="options">Kafka broker and topic configuration.</param>
    public KafkaPublisher(IOptions<KafkaOptions> options)
    {
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = "streamforge-upload-outbox",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    /// <inheritdoc />
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(message.Id.ToString("D")) },
            { "event-type", Encoding.UTF8.GetBytes(message.Type) },
            { "event-version", Encoding.UTF8.GetBytes(message.Version.ToString()) }
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

    /// <inheritdoc />
    public void Dispose() => producer.Dispose();
}
