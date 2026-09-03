using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Data;
using StreamForge.Transcoding.Worker.Data.Entities;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Implements short PostgreSQL transactions for scalable job claiming and outcomes.</summary>
public sealed class JobStore(
    IDbContextFactory<TranscodingDbContext> contextFactory,
    OutcomeMessageFactory outcomeFactory,
    IOptions<TranscodingOptions> options,
    TimeProvider timeProvider) : IJobStore
{
    private readonly TranscodingOptions transcodingOptions = options.Value;

    public async Task<LeasedJob?> ClaimNextAsync(string leaseOwner, CancellationToken cancellationToken)
    {
        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var now = timeProvider.GetUtcNow();
            var job = await dbContext.Jobs
                .FromSqlInterpolated($$"""
                    SELECT *
                    FROM transcoding.jobs
                    WHERE (
                        (status IN ('queued', 'retry-pending') AND next_attempt_at_utc <= {{now}})
                        OR (status = 'processing' AND lease_expires_at_utc <= {{now}})
                    )
                    ORDER BY next_attempt_at_utc, created_at_utc, event_id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (job is null)
            {
                return null;
            }

            job.Status = JobStatuses.Processing;
            job.AttemptCount++;
            job.LeaseOwner = leaseOwner;
            job.LeaseExpiresAtUtc = now.AddSeconds(transcodingOptions.LeaseDurationSeconds);
            job.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToLeasedJob(job);
        }, cancellationToken);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid eventId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var updated = await dbContext.Jobs
            .Where(job =>
                job.EventId == eventId &&
                job.Status == JobStatuses.Processing &&
                job.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.LeaseExpiresAtUtc, now.AddSeconds(transcodingOptions.LeaseDurationSeconds))
                .SetProperty(job => job.UpdatedAtUtc, now),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> CompleteAsync(
        LeasedJob leasedJob,
        ProcessedTranscodingResult result,
        CancellationToken cancellationToken)
    {
        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var job = await FindOwnedJobAsync(dbContext, leasedJob, cancellationToken);
            if (job is null)
            {
                return false;
            }

            var now = timeProvider.GetUtcNow();
            foreach (var rendition in result.Renditions)
            {
                dbContext.Renditions.Add(new RenditionAsset
                {
                    Id = Guid.NewGuid(),
                    JobEventId = job.EventId,
                    Tier = rendition.Tier,
                    Width = rendition.Width,
                    Height = rendition.Height,
                    VideoCodec = rendition.VideoCodec,
                    AudioCodec = rendition.AudioCodec,
                    ContentType = rendition.ContentType,
                    Bucket = rendition.Bucket,
                    ObjectKey = rendition.ObjectKey,
                    Etag = rendition.Etag,
                    SizeBytes = rendition.SizeBytes
                });
            }

            job.Status = JobStatuses.Completed;
            job.CompletedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            job.LastErrorCode = null;
            job.LastErrorMessage = null;
            dbContext.HlsPackages.Add(HlsPackageAsset.From(job.EventId, result.HlsPackage));
            dbContext.OutboxMessages.Add(outcomeFactory.CreateCompleted(job, result, now));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> ScheduleRetryAsync(
        LeasedJob leasedJob,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var delay = CalculateRetryDelay(
            leasedJob.AttemptCount,
            transcodingOptions.RetryBaseDelaySeconds,
            transcodingOptions.RetryMaximumDelaySeconds,
            Random.Shared.NextDouble());
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await dbContext.Jobs
            .Where(job =>
                job.EventId == leasedJob.EventId &&
                job.Status == JobStatuses.Processing &&
                job.LeaseOwner == leasedJob.LeaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, JobStatuses.RetryPending)
                .SetProperty(job => job.NextAttemptAtUtc, now.Add(delay))
                .SetProperty(job => job.UpdatedAtUtc, now)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.LastErrorCode, errorCode)
                .SetProperty(job => job.LastErrorMessage, Truncate(errorMessage)),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> FailAsync(
        LeasedJob leasedJob,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var job = await FindOwnedJobAsync(dbContext, leasedJob, cancellationToken);
            if (job is null)
            {
                return false;
            }

            var now = timeProvider.GetUtcNow();
            job.Status = JobStatuses.Failed;
            job.CompletedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            job.LastErrorCode = errorCode;
            job.LastErrorMessage = Truncate(errorMessage);
            dbContext.OutboxMessages.Add(outcomeFactory.CreateFailed(job, errorCode, Truncate(errorMessage), now));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public static TimeSpan CalculateRetryDelay(
        int attemptCount,
        int baseSeconds,
        int maximumSeconds,
        double jitterSample)
    {
        var exponent = Math.Min(30, Math.Max(0, attemptCount - 1));
        var baseDelay = Math.Min(maximumSeconds, baseSeconds * Math.Pow(2, exponent));
        var jitterMultiplier = 1 + (Math.Clamp(jitterSample, 0, 1) * 0.2);
        return TimeSpan.FromSeconds(Math.Min(maximumSeconds, baseDelay * jitterMultiplier));
    }

    private static async Task<TranscodingJob?> FindOwnedJobAsync(
        TranscodingDbContext dbContext,
        LeasedJob leasedJob,
        CancellationToken cancellationToken) =>
        await dbContext.Jobs
            .FromSqlInterpolated($$"""
                SELECT *
                FROM transcoding.jobs
                WHERE event_id = {{leasedJob.EventId}}
                  AND status = 'processing'
                  AND lease_owner = {{leasedJob.LeaseOwner}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<T> ExecuteInTransactionAsync<T>(
        Func<TranscodingDbContext, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(dbContext);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private static LeasedJob ToLeasedJob(TranscodingJob job) => new(
        job.EventId,
        job.VideoId,
        job.SourceBucket,
        job.SourceObjectKey,
        job.SourceEtag,
        job.SourceSizeBytes,
        job.OriginalFileName,
        job.CorrelationId,
        job.AttemptCount,
        job.CreatedAtUtc,
        job.LeaseOwner!);

    private static string Truncate(string value) => value.Length <= 2_000 ? value : value[..2_000];
}
