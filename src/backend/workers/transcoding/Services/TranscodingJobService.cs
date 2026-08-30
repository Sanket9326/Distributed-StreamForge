using System.Diagnostics;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Runs leased FFmpeg jobs independently from Kafka intake.</summary>
public sealed class TranscodingJobService(
    IJobStore jobStore,
    ITranscodingPipeline pipeline,
    StartupGate startupGate,
    TranscodingTelemetry telemetry,
    IOptions<TranscodingOptions> options,
    ILogger<TranscodingJobService> logger) : BackgroundService
{
    private readonly TranscodingOptions transcodingOptions = options.Value;
    private readonly string replicaId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupGate.WaitAsync(stoppingToken);
        var workers = Enumerable.Range(0, transcodingOptions.MaxConcurrentJobs)
            .Select(workerNumber => RunWorkerAsync(workerNumber, stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(int workerNumber, CancellationToken stoppingToken)
    {
        var leaseOwner = $"{replicaId}-{workerNumber}";
        while (!stoppingToken.IsCancellationRequested)
        {
            LeasedJob? job = null;
            try
            {
                job = await jobStore.ClaimNextAsync(leaseOwner, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(transcodingOptions.PollIntervalMilliseconds),
                        stoppingToken);
                    continue;
                }

                logger.LogInformation(
                    "Claimed transcoding event {EventId} for video {VideoId}; attempt {AttemptCount}",
                    job.EventId,
                    job.VideoId,
                    job.AttemptCount);
                telemetry.QueueAge.Record(Math.Max(0, (DateTimeOffset.UtcNow - job.CreatedAtUtc).TotalSeconds));
                await ProcessLeasedJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Transcoding worker loop failed{EventContext}",
                    job is null ? string.Empty : $" for event {job.EventId}");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ProcessLeasedJobAsync(LeasedJob job, CancellationToken stoppingToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var jobSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        jobSource.CancelAfter(TimeSpan.FromSeconds(transcodingOptions.JobTimeoutSeconds));
        var heartbeat = MaintainLeaseAsync(job, jobSource, stoppingToken);
        try
        {
            var renditions = await pipeline.ProcessAsync(job, jobSource.Token);
            if (await jobStore.CompleteAsync(job, renditions, stoppingToken))
            {
                logger.LogInformation(
                    "Completed transcoding event {EventId} for video {VideoId}",
                    job.EventId,
                    job.VideoId);
                telemetry.CompletedJobs.Add(1);
            }
            else
            {
                logger.LogWarning("Lost lease before completing transcoding event {EventId}", job.EventId);
            }
        }
        catch (PermanentTranscodingException exception)
        {
            await jobStore.FailAsync(job, exception.Code, exception.SafeMessage, stoppingToken);
            telemetry.FailedJobs.Add(1, new KeyValuePair<string, object?>("reason", exception.Code));
            logger.LogWarning(
                exception,
                "Transcoding event {EventId} failed permanently with {FailureCode}",
                job.EventId,
                exception.Code);
        }
        catch (TransientTranscodingException exception)
        {
            await HandleTransientFailureAsync(job, exception.Code, exception.SafeMessage, stoppingToken);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            await HandleTransientFailureAsync(
                job,
                "job_cancelled_or_timed_out",
                "The transcoding attempt lost its lease or exceeded its time limit.",
                stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected processing failure for event {EventId}", job.EventId);
            await HandleTransientFailureAsync(
                job,
                "unexpected_processing_failure",
                "The transcoding attempt failed unexpectedly.",
                stoppingToken);
        }
        finally
        {
            telemetry.EncodeDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
            jobSource.Cancel();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
                // Expected when processing finishes or the host stops.
            }
        }
    }

    private async Task MaintainLeaseAsync(
        LeasedJob job,
        CancellationTokenSource jobSource,
        CancellationToken stoppingToken)
    {
        while (!jobSource.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(transcodingOptions.LeaseHeartbeatSeconds), jobSource.Token);
            try
            {
                if (!await jobStore.RenewLeaseAsync(job.EventId, job.LeaseOwner, jobSource.Token))
                {
                    jobSource.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (jobSource.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Lease heartbeat failed for event {EventId}", job.EventId);
                jobSource.Cancel();
                return;
            }
        }
    }

    private async Task HandleTransientFailureAsync(
        LeasedJob job,
        string code,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        if (job.AttemptCount >= transcodingOptions.MaxAttempts)
        {
            await jobStore.FailAsync(job, code, safeMessage, cancellationToken);
            telemetry.FailedJobs.Add(1, new KeyValuePair<string, object?>("reason", code));
            logger.LogWarning(
                "Transcoding event {EventId} exhausted {AttemptCount} attempts with {FailureCode}",
                job.EventId,
                job.AttemptCount,
                code);
        }
        else
        {
            await jobStore.ScheduleRetryAsync(job, code, safeMessage, cancellationToken);
            telemetry.RetriedJobs.Add(1, new KeyValuePair<string, object?>("reason", code));
            logger.LogWarning(
                "Transcoding event {EventId} scheduled for retry after {FailureCode}; attempt {AttemptCount}",
                job.EventId,
                code,
                job.AttemptCount);
        }
    }
}
