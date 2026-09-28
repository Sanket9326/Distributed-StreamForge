using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class EngagementKafkaPublisher : IDisposable, ISubscriptionPublisher, IWatchHistoryPublisher
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

    public async Task<SubscriptionDelivery> PublishSubscriptionAsync(UserSubscriptionChangedV1 message, CancellationToken ct)
    {
        var delivered = await PublishAsync(options.SubscriptionTopic, message.Key, message.EventId,
            message.EventType, message.EventVersion, message, ct);
        return new(delivered.Partition.Value, delivered.Offset.Value);
    }

    public async Task PublishSubscriptionDeadLetterAsync(SubscriptionDeadLetterV1 message, string key, CancellationToken ct) =>
        _ = await PublishAsync(options.SubscriptionDeadLetterTopic, key, message.EventId,
            message.EventType, message.EventVersion, message, ct);

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

    public async Task<WatchHistoryDelivery> PublishWatchHistoryAsync(UserWatchProgressSavedV1 message, CancellationToken ct)
    {
        var delivery = await PublishAsync(options.WatchHistoryTopic, message.Key, message.EventId,
            message.EventType, message.EventVersion, message, ct);
        return new(delivery.Partition.Value, delivery.Offset.Value);
    }
    public async Task RepublishWatchHistoryAsync(WatchHistoryRetryEnvelope envelope, CancellationToken ct)
    {
        if (envelope.DestinationTopic != options.WatchHistoryTopic || envelope.OriginalPartition < 0 ||
            envelope.OriginalPartition >= options.WatchHistoryPartitionCount)
            throw new InvalidOperationException("Invalid watch history retry destination.");
        var headers = new Headers();
        foreach (var header in envelope.Headers.Where(x => x.Key != WatchHistoryConsumer.RetryHeader))
            headers.Add(header.Key, header.Value);
        headers.Add(WatchHistoryConsumer.RetryHeader, JsonSerializer.SerializeToUtf8Bytes(envelope, Json));
        await producer.ProduceAsync(new TopicPartition(envelope.DestinationTopic, envelope.OriginalPartition),
            new Message<string, string> { Key = envelope.Key, Value = envelope.Payload, Headers = headers }, ct);
    }
    public async Task PublishWatchHistoryDeadLetterAsync(WatchHistoryDeadLetterV1 message, string key, CancellationToken ct) =>
        _ = await PublishAsync(options.WatchHistoryDeadLetterTopic, key, message.EventId, message.EventType, message.EventVersion, message, ct);
}
