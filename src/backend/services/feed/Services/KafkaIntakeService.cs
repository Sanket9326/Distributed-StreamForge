using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class KafkaIntakeService(
    IServiceScopeFactory scopeFactory,
    StartupGate startupGate,
    CompletionNotifier completionNotifier,
    IOptions<KafkaOptions> options,
    ILogger<KafkaIntakeService> logger) : BackgroundService
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        using var consumer = CreateConsumer();
        consumer.Subscribe([kafkaOptions.UploadedTopic, kafkaOptions.CompletedTopic]);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumed = null;
                try
                {
                    consumed = consumer.Consume(stoppingToken);
                    using var scope = scopeFactory.CreateScope();
                    var projector = scope.ServiceProvider.GetRequiredService<IFeedEventProjector>();
                    var result = await projector.ProjectAsync(
                        new ConsumedEnvelope(
                            consumed.Topic,
                            consumed.Partition.Value,
                            consumed.Offset.Value,
                            consumed.Message.Key,
                            consumed.Message.Value),
                        stoppingToken);
                    if (result.CompletionRecorded)
                    {
                        completionNotifier.Publish(new CompletionNotification(
                            result.VideoId,
                            result.AvailableAtUtc));
                    }
                    consumer.Commit(consumed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException exception)
                {
                    logger.LogWarning(exception, "Kafka feed consumption failed");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (KafkaException exception) when (consumed is not null)
                {
                    logger.LogWarning(
                        exception,
                        "Kafka commit failed for {TopicPartitionOffset}; seeking for replay",
                        consumed.TopicPartitionOffset);
                    SeekForRetry(consumer, consumed);
                }
                catch (Exception exception) when (consumed is not null)
                {
                    logger.LogError(
                        exception,
                        "Feed projection failed for {TopicPartitionOffset}; seeking for retry",
                        consumed.TopicPartitionOffset);
                    SeekForRetry(consumer, consumed);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Feed intake failed before receiving an event");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private IConsumer<string, string> CreateConsumer() =>
        new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            GroupId = kafkaOptions.ConsumerGroupId,
            ClientId = $"streamforge-feed-intake-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        }).Build();

    private static void SeekForRetry(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> consumed)
    {
        try
        {
            consumer.Seek(consumed.TopicPartitionOffset);
        }
        catch (KafkaException)
        {
            // The uncommitted event will be replayed after the next rebalance.
        }
    }
}
