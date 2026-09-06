using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class EngagementKafkaPublisher : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IProducer<string, string> producer;
    private readonly KafkaOptions options;

    public EngagementKafkaPublisher(IOptions<KafkaOptions> options)
    {
        this.options = options.Value;
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = this.options.BootstrapServers,
            ClientId = $"streamforge-engagement-api-{Environment.MachineName}",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public Task<DeliveryResult<string, string>> PublishReactionAsync(
        VideoReactionChangedV1 message,
        CancellationToken cancellationToken) =>
        PublishAsync(options.ReactionTopic, message.VideoId.ToString("D"), message.EventId,
            message.EventType, message.EventVersion, message, cancellationToken);

    public Task<DeliveryResult<string, string>> PublishViewAsync(
        VideoViewQualifiedV1 message,
        CancellationToken cancellationToken) =>
        PublishAsync(options.ViewTopic, message.VideoId.ToString("D"), message.EventId,
            message.EventType, message.EventVersion, message, cancellationToken);

    private Task<DeliveryResult<string, string>> PublishAsync<T>(
        string topic,
        string key,
        Guid eventId,
        string eventType,
        int version,
        T value,
        CancellationToken cancellationToken) => producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(value, Json),
            Headers = new Headers
            {
                { "event-id", Encoding.UTF8.GetBytes(eventId.ToString("D")) },
                { "event-type", Encoding.UTF8.GetBytes(eventType) },
                { "event-version", Encoding.UTF8.GetBytes(version.ToString()) }
            }
        }, cancellationToken);

    public void Dispose() => producer.Dispose();
}
