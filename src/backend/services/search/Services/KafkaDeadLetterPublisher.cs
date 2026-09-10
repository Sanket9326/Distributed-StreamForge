using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Search.Api.Models;
using StreamForge.Search.Api.Options;

namespace StreamForge.Search.Api.Services;

public sealed class KafkaDeadLetterPublisher : ISearchDeadLetterPublisher, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IProducer<string, string> producer;
    private readonly string topic;

    public KafkaDeadLetterPublisher(IOptions<KafkaOptions> options)
    {
        topic = options.Value.DeadLetterTopic;
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = "streamforge-search-dead-letter",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public async Task PublishAsync(
        SearchIndexDeadLetterV1 deadLetter,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(deadLetter.EventId.ToString("D")) },
            { "event-type", Encoding.UTF8.GetBytes(deadLetter.EventType) },
            { "event-version", Encoding.UTF8.GetBytes(deadLetter.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
        };
        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = partitionKey,
            Value = JsonSerializer.Serialize(deadLetter, SerializerOptions),
            Headers = headers
        }, cancellationToken);
    }

    public void Dispose() => producer.Dispose();
}
