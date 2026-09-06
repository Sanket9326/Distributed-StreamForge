using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;

namespace StreamForge.Engagement.Api.Services;

public sealed class VideoCatalogConsumer(
    IDbContextFactory<EngagementDbContext> contextFactory,
    StartupGate startupGate,
    IOptions<KafkaOptions> options,
    TimeProvider timeProvider,
    ILogger<VideoCatalogConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly KafkaOptions kafka = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        using var consumer = KafkaConsumerFactory.Create(kafka.BootstrapServers, kafka.CatalogConsumerGroupId, "catalog");
        consumer.Subscribe(kafka.CompletedTopic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumed = null;
                try
                {
                    consumed = consumer.Consume(stoppingToken);
                    await ProjectAsync(consumed, stoppingToken);
                    consumer.Commit(consumed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Engagement catalog projection failed");
                    if (consumed is not null) TrySeek(consumer, consumed);
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        finally { consumer.Close(); }
    }

    private async Task ProjectAsync(ConsumeResult<string, string> consumed, CancellationToken cancellationToken)
    {
        VideoCompletedEnvelope? message;
        try { message = JsonSerializer.Deserialize<VideoCompletedEnvelope>(consumed.Message.Value, Json); }
        catch (JsonException) { return; }
        if (message is null || message.EventId == Guid.Empty || message.VideoId == Guid.Empty ||
            message.EventType != "video.transcoding.completed" || message.EventVersion is not (1 or 2)) return;

        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await dbContext.ConsumedMessages.AnyAsync(x => x.EventId == message.EventId, cancellationToken)) return;
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var video = await dbContext.Videos.SingleOrDefaultAsync(x => x.VideoId == message.VideoId, cancellationToken);
            if (video is null)
                dbContext.Videos.Add(new KnownVideo { VideoId = message.VideoId, AvailableAtUtc = message.OccurredAtUtc });
            dbContext.ConsumedMessages.Add(new ConsumedKafkaMessage
            {
                Topic = consumed.Topic, Partition = consumed.Partition.Value, Offset = consumed.Offset.Value,
                EventId = message.EventId, ConsumedAtUtc = timeProvider.GetUtcNow()
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static void TrySeek(IConsumer<string, string> consumer, ConsumeResult<string, string> consumed)
    {
        try { consumer.Seek(consumed.TopicPartitionOffset); } catch (KafkaException) { }
    }
}
