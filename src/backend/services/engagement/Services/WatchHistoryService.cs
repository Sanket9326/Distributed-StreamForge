using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryService(WatchHistoryCache cache, IWatchHistoryPublisher publisher,
    KnownVideoService videos, IDbContextFactory<EngagementDbContext> contexts, TimeProvider clock)
{
    public async Task<WatchHistoryMutation> SaveAsync(Guid user, Guid video, SaveWatchProgress request, string correlation, CancellationToken ct)
    {
        if (video == Guid.Empty || !request.IsValid)
            throw new EngagementRequestException(400, "Invalid progress", "Provide a video, positive duration, and a position within that duration.");
        await videos.EnsureAvailableAsync(video, ct);
        var now = clock.GetUtcNow();
        now = new DateTimeOffset(now.UtcTicks / 10 * 10, TimeSpan.Zero);
        var message = new UserWatchProgressSavedV1(Guid.NewGuid(), UserWatchProgressSavedV1.Type, 1,
            now, user, video, request.PositionMs!.Value, request.DurationMs!.Value, request.IsCompleted!.Value, correlation);
        var delivery = await publisher.PublishWatchHistoryAsync(message, ct);
        var state = WatchHistoryCacheState.From(new WatchHistory { UserId = user, VideoId = video,
            PositionMs = message.PositionMs, DurationMs = message.DurationMs, IsCompleted = message.IsCompleted,
            CreatedAtUtc = now, UpdatedAtUtc = now, SourcePartition = delivery.Partition, SourceOffset = delivery.Offset }, false);
        try { return new((await cache.ApplyAsync(state, ct)).Item(), false); }
        catch (Exception ex) when (ex is RedisException or TimeoutException or OperationCanceledException)
        { return new(state.Item(), true); }
    }

    public async Task<WatchHistoryItem?> GetAsync(Guid user, Guid video, CancellationToken ct)
    {
        if (video == Guid.Empty) throw new EngagementRequestException(400, "Invalid video", "Provide a non-empty video ID.");
        try { return await cache.GetAsync(user, video, ct); }
        catch (Exception ex) when (CacheFailure(ex, ct))
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var row = await db.WatchHistory.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user && x.VideoId == video, ct);
            return row is null ? null : WatchHistoryCacheState.From(row).Item();
        }
    }
    public async Task<WatchHistoryPage> GetPageAsync(Guid user, int limit, string? cursor, CancellationToken ct)
    {
        if (limit is < 1 or > 50) throw new EngagementRequestException(400, "Invalid page size", "Use a limit between 1 and 50.");
        try { return await cache.GetPageAsync(user, limit, cursor, ct); }
        catch (Exception ex) when (CacheFailure(ex, ct)) { return await cache.GetDatabasePageAsync(user, limit, cursor, ct); }
    }
    private static bool CacheFailure(Exception ex, CancellationToken ct) =>
        ex is RedisException or TimeoutException || ex is OperationCanceledException && !ct.IsCancellationRequested;
}
