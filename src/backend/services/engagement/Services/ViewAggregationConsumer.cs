using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class ViewAggregationConsumer(
    IDbContextFactory<EngagementDbContext> contextFactory,
    RedisEngagementProjection cache,
    StartupGate startupGate,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<EngagementOptions> engagementOptions,
    TimeProvider timeProvider,
    ILogger<ViewAggregationConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly KafkaOptions kafka = kafkaOptions.Value;
    private readonly EngagementOptions engagement = engagementOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        using var consumer = KafkaConsumerFactory.Create(kafka.BootstrapServers, kafka.ViewConsumerGroupId, "views");
        consumer.Subscribe(kafka.ViewTopic);
        var buffer = new List<ConsumeResult<string, string>>();
        var nextFlush = timeProvider.GetUtcNow().AddSeconds(engagement.ViewAggregationWindowSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumed = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    if (consumed is not null) buffer.Add(consumed);
                    if (ShouldFlush(buffer.Count, timeProvider.GetUtcNow(), nextFlush,
                        engagement.MaximumViewBatchSize))
                    {
                        await FlushAsync(consumer, buffer, stoppingToken);
                        buffer.Clear();
                        nextFlush = timeProvider.GetUtcNow().AddSeconds(engagement.ViewAggregationWindowSeconds);
                    }
                }
                catch (ConsumeException exception)
                { logger.LogWarning(exception, "View consumption failed"); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "View aggregation failed; buffered events will be retried");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            if (buffer.Count > 0)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await FlushAsync(consumer, buffer, timeout.Token); }
                catch (Exception exception) { logger.LogError(exception, "Final view aggregation flush failed; Kafka will replay the batch"); }
            }
            consumer.Close();
        }
    }

    internal async Task FlushAsync(
        IConsumer<string, string> consumer,
        IReadOnlyCollection<ConsumeResult<string, string>> batch,
        CancellationToken cancellationToken) =>
        await FlushAsync(batch, offsets => consumer.Commit(offsets), cancellationToken);

    internal static bool ShouldFlush(
        int bufferedCount,
        DateTimeOffset now,
        DateTimeOffset deadline,
        int maximumBatchSize) =>
        bufferedCount >= maximumBatchSize || bufferedCount > 0 && now >= deadline;

    internal async Task FlushAsync(
        IReadOnlyCollection<ConsumeResult<string, string>> batch,
        Action<IEnumerable<TopicPartitionOffset>> commit,
        CancellationToken cancellationToken)
    {
        var parsed = new List<(ConsumeResult<string, string> Consumed, VideoViewQualifiedV1 Message)>();
        foreach (var consumed in batch)
        {
            try
            {
                var message = JsonSerializer.Deserialize<VideoViewQualifiedV1>(consumed.Message.Value, Json);
                if (message is not null && message.EventId != Guid.Empty && message.VideoId != Guid.Empty &&
                    message.EventType == VideoViewQualifiedV1.Type && message.EventVersion == 1)
                    parsed.Add((consumed, message));
            }
            catch (JsonException) { }
        }

        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var durableCounts = await strategy.ExecuteAsync(async () =>
        {
            var counts = new Dictionary<Guid, long>();
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var eventIds = parsed.Select(x => x.Message.EventId).Distinct().ToArray();
            var existing = eventIds.Length == 0
                ? new HashSet<Guid>()
                : (await dbContext.ConsumedMessages.AsNoTracking()
                    .Where(x => eventIds.Contains(x.EventId)).Select(x => x.EventId)
                    .ToListAsync(cancellationToken)).ToHashSet();
            var seen = new HashSet<Guid>(existing);
            var deltas = new Dictionary<Guid, long>();
            foreach (var (consumed, message) in parsed)
            {
                if (!seen.Add(message.EventId)) continue;
                dbContext.ConsumedMessages.Add(new ConsumedKafkaMessage
                {
                    Topic = consumed.Topic, Partition = consumed.Partition.Value, Offset = consumed.Offset.Value,
                    EventId = message.EventId, ConsumedAtUtc = timeProvider.GetUtcNow()
                });
                deltas[message.VideoId] = deltas.GetValueOrDefault(message.VideoId) + 1;
            }

            foreach (var (videoId, delta) in deltas)
            {
                var aggregate = await dbContext.VideoViews.SingleOrDefaultAsync(x => x.VideoId == videoId, cancellationToken);
                if (aggregate is null)
                {
                    aggregate = new VideoView { VideoId = videoId, Count = delta, UpdatedAtUtc = timeProvider.GetUtcNow() };
                    dbContext.VideoViews.Add(aggregate);
                }
                else
                {
                    aggregate.Count += delta;
                    aggregate.UpdatedAtUtc = timeProvider.GetUtcNow();
                }
                counts[videoId] = aggregate.Count;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return counts;
        });

        var offsets = batch.GroupBy(x => x.TopicPartition)
            .Select(group => new TopicPartitionOffset(group.Key, new Offset(group.Max(x => x.Offset.Value) + 1)))
            .ToArray();
        if (offsets.Length > 0) commit(offsets);
        foreach (var (videoId, count) in durableCounts)
        {
            try { await cache.EnsureViewAtLeastAsync(videoId, count, cancellationToken); }
            catch (RedisException exception)
            { logger.LogWarning(exception, "View cache reconciliation failed for {VideoId}", videoId); }
        }
        logger.LogInformation("Aggregated {EventCount} view events across {VideoCount} videos", parsed.Count, durableCounts.Count);
    }
}
