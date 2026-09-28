using System.Diagnostics.Metrics;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryRetryWorker(WatchHistoryRetryQueue queue, IWatchHistoryPublisher publisher,
    StartupGate startup, ILogger<WatchHistoryRetryWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("StreamForge.Engagement.WatchHistory");
    private static readonly Histogram<long> Depth = Meter.CreateHistogram<long>("watch_history.retry.depth");
    private static readonly Histogram<double> Overdue = Meter.CreateHistogram<double>("watch_history.retry.overdue", "s");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var token = Guid.NewGuid().ToString("N");
                var batch = await queue.ClaimAsync(token, stoppingToken);
                // Each claimed item owns a renewal task, including while other publications are pending.
                await Task.WhenAll(batch.Select(x => PublishAsync(x, token, stoppingToken)));
                var stats = await queue.StatsAsync(stoppingToken);
                Depth.Record(stats.Depth); Overdue.Record(stats.OverdueSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Watch history retry scheduler failed: {Category}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
    public async Task PublishAsync(WatchHistoryRetryEnvelope envelope, string token, CancellationToken ct)
    {
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var renewal = RenewAsync(envelope.RetryId, token, lease);
        try
        {
            await publisher.RepublishWatchHistoryAsync(envelope, lease.Token);
            await queue.ControlAsync(envelope.RetryId, token, "ack", lease.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Watch history republication pending: {Category}", ex.GetType().Name);
            try { await queue.ControlAsync(envelope.RetryId, token, "release", ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* Lease expiry recovers the claim. */ }
        }
        finally
        {
            lease.Cancel();
            await renewal;
        }
    }
    private async Task RenewAsync(string id, string token, CancellationTokenSource lease)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(lease.Token))
                if (!await queue.ControlAsync(id, token, "renew", lease.Token)) { lease.Cancel(); return; }
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested) { }
        catch { lease.Cancel(); }
    }
}
