using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class RedisEngagementProjection(
    IConnectionMultiplexer redis,
    IDbContextFactory<EngagementDbContext> contextFactory)
{
    private const string Prefix = "streamforge:engagement:v1";
    private static readonly SemaphoreSlim HydrationLock = new(1, 1);

    private const string ApplyReactionScript = """
        local current = redis.call('HGET', KEYS[3], ARGV[1])
        if current and tonumber(current) > tonumber(ARGV[3]) then
          return {redis.call('SCARD', KEYS[1]), redis.call('SCARD', KEYS[2])}
        end
        redis.call('SREM', KEYS[1], ARGV[1])
        redis.call('SREM', KEYS[2], ARGV[1])
        if ARGV[2] == 'like' then redis.call('SADD', KEYS[1], ARGV[1]) end
        if ARGV[2] == 'dislike' then redis.call('SADD', KEYS[2], ARGV[1]) end
        redis.call('HSET', KEYS[3], ARGV[1], ARGV[3])
        redis.call('SET', KEYS[4], '1')
        return {redis.call('SCARD', KEYS[1]), redis.call('SCARD', KEYS[2])}
        """;

    private const string IncrementViewScript = """
        if redis.call('SET', KEYS[1], '1', 'NX', 'EX', ARGV[1]) then
          return {1, redis.call('INCR', KEYS[2])}
        end
        local current = redis.call('GET', KEYS[2]) or '0'
        return {0, tonumber(current)}
        """;

    private const string EnsureAtLeastScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        local durable = tonumber(ARGV[1])
        if current < durable then redis.call('SET', KEYS[1], durable); return durable end
        return current
        """;

    public async Task<VideoSummaryResponse> GetSummaryAsync(Guid videoId, CancellationToken cancellationToken)
    {
        await EnsureReactionCacheAsync(videoId, cancellationToken);
        var database = redis.GetDatabase();
        var likes = await database.SetLengthAsync(Likes(videoId)).WaitAsync(cancellationToken);
        var dislikes = await database.SetLengthAsync(Dislikes(videoId)).WaitAsync(cancellationToken);
        var viewValue = await database.StringGetAsync(Views(videoId)).WaitAsync(cancellationToken);
        var commentValue = await database.StringGetAsync(Comments(videoId)).WaitAsync(cancellationToken);

        if (viewValue.IsNull || commentValue.IsNull)
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (viewValue.IsNull)
            {
                var count = await dbContext.VideoViews.AsNoTracking()
                    .Where(x => x.VideoId == videoId).Select(x => (long?)x.Count)
                    .SingleOrDefaultAsync(cancellationToken) ?? 0;
                await database.StringSetAsync(Views(videoId), count).WaitAsync(cancellationToken);
                viewValue = count;
            }
            if (commentValue.IsNull)
            {
                var count = await dbContext.Comments.AsNoTracking().LongCountAsync(x => x.VideoId == videoId, cancellationToken);
                await database.StringSetAsync(Comments(videoId), count).WaitAsync(cancellationToken);
                commentValue = count;
            }
        }

        return new VideoSummaryResponse(videoId, likes, dislikes, (long)viewValue, (long)commentValue);
    }

    public async Task<string> GetReactionAsync(Guid videoId, Guid userId, CancellationToken cancellationToken)
    {
        await EnsureReactionCacheAsync(videoId, cancellationToken);
        var database = redis.GetDatabase();
        if (await database.SetContainsAsync(Likes(videoId), userId.ToString("D")).WaitAsync(cancellationToken)) return "like";
        if (await database.SetContainsAsync(Dislikes(videoId), userId.ToString("D")).WaitAsync(cancellationToken)) return "dislike";
        return "none";
    }

    public async Task<(long Likes, long Dislikes)> ApplyReactionAsync(
        Guid videoId,
        Guid userId,
        string reaction,
        long kafkaOffset,
        CancellationToken cancellationToken)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            ApplyReactionScript,
            [Likes(videoId), Dislikes(videoId), ReactionVersions(videoId), ReactionReady(videoId)],
            [userId.ToString("D"), reaction, kafkaOffset]).WaitAsync(cancellationToken);
        var values = (RedisResult[])result!;
        return ((long)values[0], (long)values[1]);
    }

    public async Task<(bool Counted, long Count)> IncrementViewOnceAsync(
        Guid videoId,
        Guid sessionId,
        int ttlHours,
        CancellationToken cancellationToken)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            IncrementViewScript,
            [ViewSession(sessionId), Views(videoId)],
            [(long)TimeSpan.FromHours(ttlHours).TotalSeconds]).WaitAsync(cancellationToken);
        var values = (RedisResult[])result!;
        return ((long)values[0] == 1, (long)values[1]);
    }

    public Task SetCommentCountAsync(Guid videoId, long count) =>
        redis.GetDatabase().StringSetAsync(Comments(videoId), count);

    public async Task EnsureViewAtLeastAsync(Guid videoId, long durableCount, CancellationToken cancellationToken) =>
        _ = await redis.GetDatabase().ScriptEvaluateAsync(
            EnsureAtLeastScript,
            [Views(videoId)],
            [durableCount]).WaitAsync(cancellationToken);

    private async Task EnsureReactionCacheAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        if (await database.KeyExistsAsync(ReactionReady(videoId)).WaitAsync(cancellationToken)) return;

        await HydrationLock.WaitAsync(cancellationToken);
        try
        {
            if (await database.KeyExistsAsync(ReactionReady(videoId)).WaitAsync(cancellationToken)) return;
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var reactions = await dbContext.Reactions.AsNoTracking()
                .Where(x => x.VideoId == videoId).ToListAsync(cancellationToken);

            await database.KeyDeleteAsync([Likes(videoId), Dislikes(videoId), ReactionVersions(videoId)]).WaitAsync(cancellationToken);
            foreach (var reaction in reactions)
            {
                var member = reaction.UserId.ToString("D");
                await database.SetAddAsync(reaction.Value == "like" ? Likes(videoId) : Dislikes(videoId), member)
                    .WaitAsync(cancellationToken);
                await database.HashSetAsync(ReactionVersions(videoId), member, reaction.SourceOffset)
                    .WaitAsync(cancellationToken);
            }
            await database.StringSetAsync(ReactionReady(videoId), "1").WaitAsync(cancellationToken);
        }
        finally
        {
            HydrationLock.Release();
        }
    }

    private static RedisKey Likes(Guid id) => $"{Prefix}:videos:{id:D}:likes";
    private static RedisKey Dislikes(Guid id) => $"{Prefix}:videos:{id:D}:dislikes";
    private static RedisKey ReactionVersions(Guid id) => $"{Prefix}:videos:{id:D}:reaction-offsets";
    private static RedisKey ReactionReady(Guid id) => $"{Prefix}:videos:{id:D}:reactions-ready";
    private static RedisKey Views(Guid id) => $"{Prefix}:videos:{id:D}:views";
    private static RedisKey Comments(Guid id) => $"{Prefix}:videos:{id:D}:comments";
    private static RedisKey ViewSession(Guid id) => $"{Prefix}:view-sessions:{id:D}";
}
