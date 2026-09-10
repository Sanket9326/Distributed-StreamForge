using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class OutboxPublisherService(
    IDbContextFactory<FeedDbContext> contextFactory,
    IFeedOutboxPublisher publisher,
    StartupGate startupGate,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private readonly OutboxOptions outboxOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var foundAny = false;
            try
            {
                foundAny = await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Feed search outbox polling failed");
            }

            if (!foundAny)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(outboxOptions.PollIntervalMilliseconds), stoppingToken);
            }
        }
    }

    private async Task<bool> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($$"""
                    SELECT *
                    FROM feed.outbox_messages
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
                        "Published search indexing event {EventId} for video {VideoId} revision {Revision}",
                        message.Id,
                        message.VideoId,
                        message.Revision);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    message.AttemptCount++;
                    message.NextAttemptAtUtc = timeProvider.GetUtcNow().Add(
                        CalculateRetryDelay(message.AttemptCount, outboxOptions.MaximumRetryDelaySeconds));
                    message.LastError = exception.Message.Length <= 2_000
                        ? exception.Message
                        : exception.Message[..2_000];
                    logger.LogWarning(exception,
                        "Search event publication failed for {EventId}; attempt {AttemptCount}",
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

    public static TimeSpan CalculateRetryDelay(int attemptCount, int maximumSeconds)
    {
        var exponent = Math.Min(30, Math.Max(0, attemptCount - 1));
        return TimeSpan.FromSeconds(Math.Min(maximumSeconds, Math.Pow(2, exponent)));
    }
}
