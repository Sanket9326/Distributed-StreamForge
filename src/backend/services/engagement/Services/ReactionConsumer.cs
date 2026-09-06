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

public sealed class ReactionConsumer(
    IDbContextFactory<EngagementDbContext> contextFactory,
    RedisEngagementProjection cache,
    StartupGate startupGate,
    IOptions<KafkaOptions> options,
    TimeProvider timeProvider,
    ILogger<ReactionConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly KafkaOptions kafka = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        using var consumer = KafkaConsumerFactory.Create(kafka.BootstrapServers, kafka.ReactionConsumerGroupId, "reactions");
        consumer.Subscribe(kafka.ReactionTopic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumed = null;
                try
                {
                    consumed = consumer.Consume(stoppingToken);
                    var message = await ProjectAsync(consumed, stoppingToken);
                    consumer.Commit(consumed);
                    if (message is not null)
                    {
                        try
                        {
                            await cache.ApplyReactionAsync(message.VideoId, message.UserId, message.Reaction,
                                consumed.Offset.Value, stoppingToken);
                        }
                        catch (RedisException exception)
                        {
                            logger.LogWarning(exception, "Reaction cache reconciliation failed for {VideoId}", message.VideoId);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Reaction projection failed");
                    if (consumed is not null) TrySeek(consumer, consumed);
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        finally { consumer.Close(); }
    }

    private async Task<VideoReactionChangedV1?> ProjectAsync(
        ConsumeResult<string, string> consumed,
        CancellationToken cancellationToken)
    {
        VideoReactionChangedV1? message;
        try { message = JsonSerializer.Deserialize<VideoReactionChangedV1>(consumed.Message.Value, Json); }
        catch (JsonException) { return null; }
        if (message is null || message.EventId == Guid.Empty || message.VideoId == Guid.Empty || message.UserId == Guid.Empty ||
            message.EventType != VideoReactionChangedV1.Type || message.EventVersion != 1 ||
            message.Reaction is not ("like" or "dislike" or "none")) return null;

        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await dbContext.ConsumedMessages.AnyAsync(x => x.EventId == message.EventId, cancellationToken)) return null;
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var current = await dbContext.Reactions.SingleOrDefaultAsync(
                x => x.VideoId == message.VideoId && x.UserId == message.UserId, cancellationToken);
            if (message.Reaction == "none")
            {
                if (current is not null) dbContext.Reactions.Remove(current);
            }
            else if (current is null)
            {
                dbContext.Reactions.Add(new Reaction
                {
                    VideoId = message.VideoId, UserId = message.UserId, Value = message.Reaction,
                    CreatedAtUtc = message.OccurredAtUtc, UpdatedAtUtc = message.OccurredAtUtc,
                    SourcePartition = consumed.Partition.Value, SourceOffset = consumed.Offset.Value
                });
            }
            else
            {
                current.Value = message.Reaction;
                current.UpdatedAtUtc = message.OccurredAtUtc;
                current.SourcePartition = consumed.Partition.Value;
                current.SourceOffset = consumed.Offset.Value;
            }
            dbContext.ConsumedMessages.Add(new ConsumedKafkaMessage
            {
                Topic = consumed.Topic, Partition = consumed.Partition.Value, Offset = consumed.Offset.Value,
                EventId = message.EventId, ConsumedAtUtc = timeProvider.GetUtcNow()
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return message;
        });
    }

    private static void TrySeek(IConsumer<string, string> consumer, ConsumeResult<string, string> consumed)
    {
        try { consumer.Seek(consumed.TopicPartitionOffset); } catch (KafkaException) { }
    }
}
