using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StreamForge.Upload.Api.Data;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Claims pending outbox rows, publishes them to Kafka, and records acknowledgement or retry state.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IKafkaPublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private readonly OutboxOptions outboxOptions = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var publishedAny = false;
            try
            {
                publishedAny = await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox polling failed");
            }

            if (!publishedAny)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(outboxOptions.PollIntervalMilliseconds),
                    stoppingToken);
            }
        }
    }

    /// <summary>Calculates a bounded exponential delay for a failed publication attempt.</summary>
    /// <param name="attemptCount">The one-based failure attempt count.</param>
    /// <param name="maximumSeconds">The configured upper bound in seconds.</param>
    /// <returns>The delay before the message becomes eligible for another attempt.</returns>
    public static TimeSpan CalculateRetryDelay(int attemptCount, int maximumSeconds)
    {
        var exponent = Math.Min(30, Math.Max(0, attemptCount - 1));
        return TimeSpan.FromSeconds(Math.Min(maximumSeconds, Math.Pow(2, exponent)));
    }

    private async Task<bool> PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();

            var messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($$"""
                    SELECT *
                    FROM outbox_messages
                    WHERE processed_at_utc IS NULL
                      AND next_attempt_at_utc <= {{now}}
                    ORDER BY occurred_at_utc, id
                    LIMIT {{outboxOptions.BatchSize}}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    await publisher.PublishAsync(message, cancellationToken);
                    message.ProcessedAtUtc = timeProvider.GetUtcNow();
                    message.LastError = null;
                    logger.LogInformation(
                        "Published outbox event {EventId} for video {VideoId}",
                        message.Id,
                        message.VideoId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    message.AttemptCount++;
                    message.NextAttemptAtUtc = timeProvider.GetUtcNow().Add(
                        CalculateRetryDelay(
                            message.AttemptCount,
                            outboxOptions.MaximumRetryDelaySeconds));
                    message.LastError = Truncate(exception.Message, 2_000);
                    logger.LogWarning(
                        exception,
                        "Kafka publication failed for outbox event {EventId}; attempt {AttemptCount}",
                        message.Id,
                        message.AttemptCount);
                    break;
                }
            }

            if (messages.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return messages.Count > 0;
        });
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
