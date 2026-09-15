using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class SubscriptionConsumer(SubscriptionProjector projector, SubscriptionCache cache,
    ISubscriptionPublisher publisher, StartupGate startup, IOptions<KafkaOptions> options,
    TimeProvider clock, ILogger<SubscriptionConsumer> logger) : BackgroundService
{
    public static UserSubscriptionChangedV1? Parse(string? key, string? payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("isActive", out var active) ||
                active.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            var message = JsonSerializer.Deserialize<UserSubscriptionChangedV1>(payload, SubscriptionCacheState.Json);
            return message is not null && message.EventId != Guid.Empty &&
                message.EventType == UserSubscriptionChangedV1.Type && message.EventVersion == 1 &&
                message.SubscriberId != Guid.Empty && message.CreatorId != Guid.Empty && message.SubscriberId != message.CreatorId &&
                message.OccurredAtUtc != default && !string.IsNullOrWhiteSpace(message.CorrelationId) &&
                message.Key == key && (message.ActorId == message.SubscriberId || !message.IsActive && message.ActorId == message.CreatorId)
                ? message : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task HandleAsync(ConsumeResult<string, string> consumed, CancellationToken ct)
    {
        var message = Parse(consumed.Message.Key, consumed.Message.Value);
        if (message is null)
        {
            await publisher.PublishSubscriptionDeadLetterAsync(new(Guid.NewGuid(), SubscriptionDeadLetterV1.Type, 1,
                clock.GetUtcNow(), consumed.Topic, consumed.Partition.Value, consumed.Offset.Value,
                consumed.Message.Key, "invalid_subscription_event", consumed.Message.Value ?? ""),
                consumed.Message.Key ?? Guid.NewGuid().ToString("D"), ct);
            logger.LogWarning("Subscription event dead-lettered at {Partition}:{Offset}", consumed.Partition.Value, consumed.Offset.Value);
            return;
        }
        var row = await projector.ProjectAsync(message, consumed.Topic, consumed.Partition.Value, consumed.Offset.Value, ct);
        if (row is not null) await cache.ApplyAsync(SubscriptionCacheState.From(row), ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.WaitAsync(stoppingToken);
        var kafka = options.Value;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var consumer = KafkaConsumerFactory.Create(kafka.BootstrapServers, kafka.SubscriptionConsumerGroupId, "subscriptions");
            consumer.Subscribe(kafka.SubscriptionTopic);
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, string>? record = null;
                    try
                    {
                        record = consumer.Consume(stoppingToken);
                        await HandleAsync(record, stoppingToken);
                        consumer.Commit(record);
                        failures = 0;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception exception)
                    {
                        logger.LogWarning("Subscription processing retry: {Category}", exception.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(++failures, 5)))), stoppingToken);
                        if (record is not null)
                        {
                            try { consumer.Seek(record.TopicPartitionOffset); }
                            // Rejoin from committed offsets if ownership was lost; never skip a failed record.
                            catch (KafkaException) { break; }
                        }
                    }
                }
            }
            finally
            {
                try { consumer.Close(); }
                catch (KafkaException) { logger.LogWarning("Subscription consumer closed after Kafka ownership changed"); }
            }
        }
    }
}
