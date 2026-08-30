using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Consumes upload events and commits only after durable ingestion.</summary>
public sealed class KafkaIntakeService(
    IServiceScopeFactory scopeFactory,
    StartupGate startupGate,
    IOptions<KafkaOptions> options,
    ILogger<KafkaIntakeService> logger) : BackgroundService
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        using var consumer = CreateConsumer();
        consumer.Subscribe(kafkaOptions.InputTopic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumed = null;
                try
                {
                    consumed = consumer.Consume(stoppingToken);
                    using var scope = scopeFactory.CreateScope();
                    var ingestor = scope.ServiceProvider.GetRequiredService<IMessageIngestor>();
                    await ingestor.IngestAsync(
                        new ConsumedEnvelope(
                            consumed.Topic,
                            consumed.Partition.Value,
                            consumed.Offset.Value,
                            consumed.Message.Key,
                            consumed.Message.Value),
                        stoppingToken);
                    consumer.Commit(consumed);
                }
                catch (ConsumeException exception)
                {
                    logger.LogWarning(exception, "Kafka input consumption failed");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (KafkaException exception) when (consumed is not null)
                {
                    logger.LogWarning(
                        exception,
                        "Kafka offset commit failed for {TopicPartitionOffset}; the event may be replayed",
                        consumed.TopicPartitionOffset);
                    SeekForRetry(consumer, consumed, logger);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (consumed is not null)
                {
                    logger.LogError(
                        exception,
                        "Could not durably ingest {TopicPartitionOffset}; seeking for retry",
                        consumed.TopicPartitionOffset);
                    SeekForRetry(consumer, consumed, logger);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Kafka intake loop failed before receiving an event");
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
            ClientId = $"streamforge-transcoding-intake-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        }).Build();

    private static void SeekForRetry(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> consumed,
        ILogger logger)
    {
        try
        {
            consumer.Seek(consumed.TopicPartitionOffset);
        }
        catch (KafkaException seekException)
        {
            logger.LogWarning(
                seekException,
                "Could not seek {TopicPartitionOffset}; a rebalance will safely replay the uncommitted event",
                consumed.TopicPartitionOffset);
        }
    }
}
