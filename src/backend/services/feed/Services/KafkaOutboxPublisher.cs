using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class KafkaOutboxPublisher : IFeedOutboxPublisher, IDisposable
{
    private readonly IProducer<string, string> producer;
    private readonly string searchIndexTopic;

    public KafkaOutboxPublisher(IOptions<KafkaOptions> options)
    {
        searchIndexTopic = options.Value.SearchIndexTopic;
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = "streamforge-feed-search-outbox",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Topic, searchIndexTopic, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Feed cannot publish an outbox event to '{message.Topic}'.");
        }

        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(message.Id.ToString("D")) },
            { "event-type", Encoding.UTF8.GetBytes(message.Type) },
            { "event-version", Encoding.UTF8.GetBytes(message.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
        };
        await producer.ProduceAsync(message.Topic, new Message<string, string>
        {
            Key = message.PartitionKey,
            Value = message.Payload,
            Headers = headers
        }, cancellationToken);
    }

    public void Dispose() => producer.Dispose();
}
