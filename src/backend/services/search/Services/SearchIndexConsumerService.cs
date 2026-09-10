using Confluent.Kafka;
using Microsoft.Extensions.Options;
using StreamForge.Search.Api.Options;

namespace StreamForge.Search.Api.Services;

public sealed class SearchIndexConsumerService(
    IndexingMessageHandler handler,
    StartupGate startupGate,
    IOptions<KafkaOptions> options,
    ILogger<SearchIndexConsumerService> logger) : BackgroundService
{
    private readonly KafkaOptions kafkaOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            GroupId = kafkaOptions.ConsumerGroupId,
            ClientId = $"streamforge-search-index-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        }).Build();
        consumer.Subscribe(kafkaOptions.InputTopic);
        var retryAttempts = new Dictionary<TopicPartitionOffset, int>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumed = null;
                try
                {
                    consumed = consumer.Consume(stoppingToken);
                    var position = consumed.TopicPartitionOffset;
                    var result = await handler.HandleAsync(new SearchConsumedEnvelope(
                        consumed.Topic,
                        consumed.Partition.Value,
                        consumed.Offset.Value,
                        consumed.Message.Key,
                        consumed.Message.Value ?? string.Empty), stoppingToken);
                    if (result == IndexingHandleResult.Completed)
                    {
                        consumer.Commit(consumed);
                        retryAttempts.Remove(position);
                        continue;
                    }

                    var attempt = retryAttempts.TryGetValue(position, out var previous) ? previous + 1 : 1;
                    retryAttempts[position] = attempt;
                    consumer.Seek(position);
                    var delay = CalculateRetryDelay(attempt, kafkaOptions.MaximumRetryDelaySeconds);
                    logger.LogWarning(
                        "Search indexing will retry {TopicPartitionOffset} in {Delay}; Kafka offset remains uncommitted",
                        position,
                        delay);
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException exception)
                {
                    logger.LogWarning(exception, "Kafka search intake failed");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (KafkaException exception) when (consumed is not null)
                {
                    logger.LogWarning(exception,
                        "Kafka commit or seek failed for {TopicPartitionOffset}",
                        consumed.TopicPartitionOffset);
                    TrySeek(consumer, consumed.TopicPartitionOffset);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception exception) when (consumed is not null)
                {
                    logger.LogError(exception,
                        "Unexpected search intake error for {TopicPartitionOffset}; offset remains uncommitted",
                        consumed.TopicPartitionOffset);
                    TrySeek(consumer, consumed.TopicPartitionOffset);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    public static TimeSpan CalculateRetryDelay(int attempt, int maximumSeconds)
    {
        var exponent = Math.Min(30, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(maximumSeconds, Math.Pow(2, exponent)));
    }

    private static void TrySeek(IConsumer<string, string> consumer, TopicPartitionOffset position)
    {
        try
        {
            consumer.Seek(position);
        }
        catch (KafkaException)
        {
        }
    }
}
