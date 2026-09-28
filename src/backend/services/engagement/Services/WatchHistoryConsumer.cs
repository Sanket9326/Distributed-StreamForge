using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryConsumer(WatchHistoryProjector projector, WatchHistoryCache cache,
    WatchHistoryRetryQueue retries, IWatchHistoryPublisher publisher, StartupGate startup,
    IOptions<KafkaOptions> options, TimeProvider clock, ILogger<WatchHistoryConsumer> logger) : BackgroundService
{
    public const string RetryHeader = "watch-history-retry-v1";
    private static readonly Meter Meter = new("StreamForge.Engagement.WatchHistory.Consumer");
    private static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("watch_history.dead_letters");
    private static readonly Histogram<long> Lag = Meter.CreateHistogram<long>("watch_history.consumer.lag", "records");

    public static UserWatchProgressSavedV1? Parse(string? key, string? payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("positionMs", out _) ||
                !root.TryGetProperty("durationMs", out _) || !root.TryGetProperty("isCompleted", out var completed) ||
                completed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            var m = JsonSerializer.Deserialize<UserWatchProgressSavedV1>(payload, WatchHistoryCacheState.Json);
            return m is not null && m.EventId != Guid.Empty && m.EventType == UserWatchProgressSavedV1.Type &&
                m.EventVersion == 1 && m.UserId != Guid.Empty && m.VideoId != Guid.Empty && m.Key == key &&
                m.OccurredAtUtc != default && !string.IsNullOrWhiteSpace(m.CorrelationId) &&
                new SaveWatchProgress(m.PositionMs, m.DurationMs, m.IsCompleted).IsValid ? m : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task HandleAsync(ConsumeResult<string, string> record, CancellationToken ct)
    {
        var message = Parse(record.Message.Key, record.Message.Value);
        WatchHistoryRetryEnvelope? previous = null;
        var originalOffset = record.Offset.Value;
        var validRetry = true;
        try
        {
            var headers = record.Message.Headers?.Where(x => x.Key == RetryHeader).ToArray() ?? [];
            if (headers.Length > 1) validRetry = false;
            if (headers.Length == 1)
            {
                previous = JsonSerializer.Deserialize<WatchHistoryRetryEnvelope>(headers[0].GetValueBytes(), WatchHistoryCacheState.Json);
                validRetry = previous is not null && previous.EventId == message?.EventId &&
                    previous.DestinationTopic == options.Value.WatchHistoryTopic && previous.DestinationTopic == record.Topic &&
                    previous.OriginalPartition == record.Partition.Value && previous.Key == record.Message.Key &&
                    previous.Payload == record.Message.Value && previous.Attempt is >= 1 and <= WatchHistoryRetryQueue.MaximumRetries &&
                    previous.FirstFailedAtUtc != default && previous.Headers is not null &&
                    long.TryParse(previous.OriginalOffset, NumberStyles.None, CultureInfo.InvariantCulture, out originalOffset) &&
                    originalOffset >= 0 && originalOffset < record.Offset.Value;
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { validRetry = false; }
        if (message is null || !validRetry)
        {
            await DeadLetterAsync(record, null, null, record.Offset.Value, "invalid_watch_history_event", ct);
            return;
        }
        try
        {
            var row = await projector.ProjectAsync(message, record.Topic, record.Partition.Value, originalOffset, ct);
            await cache.ApplyAsync(WatchHistoryCacheState.From(row), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var attempt = (previous?.Attempt ?? 0) + 1;
            if (attempt > WatchHistoryRetryQueue.MaximumRetries)
            {
                await DeadLetterAsync(record, message, previous, originalOffset, "processing_retries_exhausted", ct);
                return;
            }
            var now = clock.GetUtcNow();
            var envelope = new WatchHistoryRetryEnvelope(message.EventId, record.Message.Value,
                previous?.Headers ?? record.Message.Headers?.Where(x => x.Key != RetryHeader)
                    .Select(x => new WatchHistoryHeader(x.Key, x.GetValueBytes())).ToArray() ?? [],
                message.Key, record.Topic, record.Partition.Value, originalOffset.ToString(CultureInfo.InvariantCulture), attempt,
                (now + WatchHistoryRetryQueue.Delay(attempt, Random.Shared.NextDouble())).ToUnixTimeMilliseconds(),
                previous?.FirstFailedAtUtc ?? now, ex.GetType().Name);
            // Failure here leaves the source uncommitted. Redis-only retry durability is an explicit v1 tradeoff.
            await retries.EnqueueAsync(envelope, ct);
        }
    }

    private async Task DeadLetterAsync(ConsumeResult<string, string> record, UserWatchProgressSavedV1? message,
        WatchHistoryRetryEnvelope? retry, long offset, string reason, CancellationToken ct)
    {
        await publisher.PublishWatchHistoryDeadLetterAsync(new(Guid.NewGuid(), WatchHistoryDeadLetterV1.Type, 1,
            clock.GetUtcNow(), record.Topic, record.Partition.Value, offset.ToString(CultureInfo.InvariantCulture),
            record.Message.Key, reason, record.Message.Value ?? "", retry), record.Message.Key ?? Guid.NewGuid().ToString("D"), ct);
        if (message is not null) await cache.RejectAsync(message.UserId, message.VideoId, record.Partition.Value,
            offset.ToString(CultureInfo.InvariantCulture), ct);
        DeadLetters.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.WaitAsync(stoppingToken);
        var kafka = options.Value;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var consumer = KafkaConsumerFactory.Create(kafka.BootstrapServers, kafka.WatchHistoryConsumerGroupId, "watch-history");
            consumer.Subscribe(kafka.WatchHistoryTopic);
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
                        var high = consumer.GetWatermarkOffsets(record.TopicPartition).High.Value;
                        if (high >= 0) Lag.Record(Math.Max(0, high - record.Offset.Value - 1),
                            new KeyValuePair<string, object?>("partition", record.Partition.Value));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Watch history source remains uncommitted: {Category}", ex.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(++failures, 5)))), stoppingToken);
                        if (record is not null)
                        {
                            try { consumer.Seek(record.TopicPartitionOffset); }
                            catch (KafkaException) { break; }
                        }
                    }
                }
            }
            finally { try { consumer.Close(); } catch (KafkaException) { } }
        }
    }
}
