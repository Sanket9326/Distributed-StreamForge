using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Infrastructure.Redis;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class RedisEngagementProjection(
    IConnectionMultiplexer redis,
    IDbContextFactory<EngagementDbContext> contextFactory,
    ILogger<LuaScriptExecutor>? logger = null)
{
    private const string Prefix = "streamforge:engagement:v1";
    private readonly LuaScriptExecutor scripts = new(redis, logger);

    public async Task<VideoSummaryResponse> GetSummaryAsync(Guid videoId, CancellationToken ct)
    {
        await EnsureReactionCacheAsync(videoId, ct);
        var values = (RedisResult[])(await scripts.ExecuteAsync("summary-read",
            [Likes(videoId), Dislikes(videoId), Views(videoId), Comments(videoId)], [], ct))!;
        if (values[2].IsNull || values[3].IsNull)
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            if (values[2].IsNull)
            {
                var count = await db.VideoViews.AsNoTracking().Where(x => x.VideoId == videoId)
                    .Select(x => (long?)x.Count).SingleOrDefaultAsync(ct) ?? 0;
                values[2] = await WriteCountAsync(Views(videoId), count, "initialize", ct);
            }
            if (values[3].IsNull)
            {
                values[3] = RedisResult.Create((RedisValue)await ReadCommentCountAsync(videoId, ct));
            }
        }
        return new(videoId, (long)values[0], (long)values[1], (long)values[2], (long)values[3]);
    }

    public async Task<string> GetReactionAsync(Guid videoId, Guid userId, CancellationToken ct)
    {
        await EnsureReactionCacheAsync(videoId, ct);
        return (string)(await scripts.ExecuteAsync("reaction-read", [Likes(videoId), Dislikes(videoId)],
            [userId.ToString("D")], ct))!;
    }

    public async Task<(long Likes, long Dislikes)> ApplyReactionAsync(Guid videoId, Guid userId,
        string reaction, long kafkaOffset, CancellationToken ct)
    {
        var values = (RedisResult[])(await scripts.ExecuteAsync("reaction-apply",
            [Likes(videoId), Dislikes(videoId), ReactionVersions(videoId)],
            [userId.ToString("D"), reaction, kafkaOffset.ToString(CultureInfo.InvariantCulture)], ct))!;
        return ((long)values[0], (long)values[1]);
    }

    public async Task<(bool Counted, long Count)> IncrementViewOnceAsync(Guid videoId, Guid sessionId,
        int ttlHours, CancellationToken ct)
    {
        var values = (RedisResult[])(await scripts.ExecuteAsync("view-increment",
            [ViewSession(sessionId), Views(videoId)], [(long)TimeSpan.FromHours(ttlHours).TotalSeconds], ct))!;
        return ((int)values[0] == 1, (long)values[1]);
    }

    public async Task InvalidateCommentCountAsync(Guid videoId) =>
        _ = await scripts.ExecuteAsync("comment-invalidate", [Comments(videoId), CommentGeneration(videoId)],
            [Guid.NewGuid().ToString("N")], CancellationToken.None);

    private async Task<long> ReadCommentCountAsync(Guid videoId, CancellationToken ct)
    {
        long count = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var read = (RedisResult[])(await scripts.ExecuteAsync("comment-read",
                [Comments(videoId), CommentGeneration(videoId)], [], ct))!;
            if (!read[0].IsNull) return (long)read[0];
            var generation = (string)read[1]!;
            if (generation.Length == 0)
                generation = (string)(await scripts.ExecuteAsync("comment-begin",
                    [Comments(videoId), CommentGeneration(videoId)], [Guid.NewGuid().ToString("N")], ct))!;
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            count = await db.Comments.LongCountAsync(x => x.VideoId == videoId, ct);
            var initialized = await scripts.ExecuteAsync("comment-initialize",
                [Comments(videoId), CommentGeneration(videoId)],
                [generation, count.ToString(CultureInfo.InvariantCulture)], ct);
            if (!initialized.IsNull) return (long)initialized;
        }
        return count; // A busy video can return its latest database read without publishing a stale cache.
    }

    public async Task EnsureViewAtLeastAsync(Guid videoId, long durableCount, CancellationToken ct) =>
        _ = await WriteCountAsync(Views(videoId), durableCount, "maximum", ct);

    private Task<RedisResult> WriteCountAsync(RedisKey key, long count, string mode, CancellationToken ct) =>
        scripts.ExecuteAsync("count-write", [key], [count.ToString(CultureInfo.InvariantCulture), mode], ct);

    private Task EnsureReactionCacheAsync(Guid videoId, CancellationToken ct)
    {
        RedisKey[] controls = [$"{ReactionReady(videoId)}:lease", $"{ReactionReady(videoId)}:generation", ReactionReady(videoId)];
        return new CacheRebuilder(scripts).EnsureAsync(controls, async (token, cancel) =>
        {
            await using var strategyContext = await contextFactory.CreateDbContextAsync(cancel);
            await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await contextFactory.CreateDbContextAsync(cancel);
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancel);
                Guid? last = null;
                while (true)
                {
                    var query = db.Reactions.AsNoTracking().Where(x => x.VideoId == videoId);
                    if (last is { } id) query = query.Where(x => x.UserId.CompareTo(id) > 0);
                    var batch = await query.OrderBy(x => x.UserId).Take(500).ToListAsync(cancel);
                    if (batch.Count == 0) break;
                    var args = new List<RedisValue> { token };
                    foreach (var item in batch)
                        args.AddRange([item.UserId.ToString("D"), item.Value, item.SourceOffset.ToString(CultureInfo.InvariantCulture)]);
                    if ((int)await scripts.ExecuteAsync("reaction-merge",
                        [Likes(videoId), Dislikes(videoId), ReactionVersions(videoId), controls[0], controls[1]], args.ToArray(), cancel) != 1)
                        throw new RedisServerException("CACHE_REBUILD_LOST");
                    last = batch[^1].UserId;
                }
                await transaction.CommitAsync(cancel);
            });
        }, ct);
    }

    private static RedisKey Likes(Guid id) => $"{Prefix}:videos:{id:D}:likes";
    private static RedisKey Dislikes(Guid id) => $"{Prefix}:videos:{id:D}:dislikes";
    private static RedisKey ReactionVersions(Guid id) => $"{Prefix}:videos:{id:D}:reaction-offsets";
    private static RedisKey ReactionReady(Guid id) => $"{Prefix}:videos:{id:D}:reactions-ready";
    private static RedisKey Views(Guid id) => $"{Prefix}:videos:{id:D}:views";
    private static RedisKey Comments(Guid id) => $"{Prefix}:videos:{id:D}:comments";
    private static RedisKey CommentGeneration(Guid id) => $"{Prefix}:videos:{id:D}:comments-generation";
    private static RedisKey ViewSession(Guid id) => $"{Prefix}:view-sessions:{id:D}";
}
