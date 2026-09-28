using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Infrastructure.Redis;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryCache(LuaScriptExecutor scripts, IDbContextFactory<EngagementDbContext> contexts,
    WatchHistoryCursorCodec cursors)
{
    public static RedisKey StateKey(Guid user) => $"streamforge:engagement:history:v1:user:{user:D}:state";
    public static RedisKey OrderKey(Guid user) => $"streamforge:engagement:history:v1:user:{user:D}:order";
    public static RedisKey ReadyKey(Guid user) => $"streamforge:engagement:history:v1:user:{user:D}:ready";
    public static RedisKey AbsentKey(Guid user, Guid video) => $"streamforge:engagement:history:v1:user:{user:D}:absent:{video:D}";
    public async Task<WatchHistoryCacheState> ApplyAsync(WatchHistoryCacheState state, CancellationToken ct) =>
        Parse(await scripts.ExecuteAsync("history-apply", [StateKey(state.UserId), OrderKey(state.UserId), AbsentKey(state.UserId, state.VideoId)],
            [state.VideoId.ToString("D"), JsonSerializer.Serialize(state, WatchHistoryCacheState.Json)], ct));

    public async Task RejectAsync(Guid user, Guid video, int partition, string offset, CancellationToken ct) =>
        _ = await scripts.ExecuteAsync("history-reject", [StateKey(user), OrderKey(user), ReadyKey(user), $"{ReadyKey(user)}:generation"],
            [video.ToString("D"), partition, offset, Guid.NewGuid().ToString("N")], ct);

    public async Task<WatchHistoryItem?> GetAsync(Guid user, Guid video, CancellationToken ct)
    {
        var result = await scripts.ExecuteAsync("history-read", [StateKey(user), AbsentKey(user, video)], [video.ToString("D")], ct);
        if (!result.IsNull) return (string?)result == "absent" ? null : Parse(result).Item();
        await using var db = await contexts.CreateDbContextAsync(ct);
        var row = await db.WatchHistory.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user && x.VideoId == video, ct);
        if (row is not null) return (await ApplyAsync(WatchHistoryCacheState.From(row), ct)).Item();
        result = await scripts.ExecuteAsync("history-absent", [StateKey(user), AbsentKey(user, video)], [video.ToString("D")], ct);
        return (string?)result == "absent" ? null : Parse(result).Item();
    }

    public async Task<WatchHistoryPage> GetPageAsync(Guid user, int limit, string? cursor, CancellationToken ct)
    {
        var after = cursors.Decode(user, cursor);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await HydrateAsync(user, ct);
            var result = (RedisResult[])(await scripts.ExecuteAsync("history-page",
                [StateKey(user), OrderKey(user), ReadyKey(user)], [limit, after ?? ""], ct))!;
            if ((int)result[0] != 1) continue;
            return new(((RedisResult[])result[2])!.Select(x => Parse(x).Item()).ToArray(),
                result[1].IsNull ? null : cursors.Encode(user, (string)result[1]!));
        }
        throw new RedisServerException("CACHE_REBUILD_LOST");
    }

    public async Task<WatchHistoryPage> GetDatabasePageAsync(Guid user, int limit, string? cursor, CancellationToken ct)
    {
        var after = cursors.Decode(user, cursor);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var query = db.WatchHistory.AsNoTracking().Where(x => x.UserId == user);
        if (after is not null)
        {
            var time = new DateTimeOffset(long.Parse(after[..19], CultureInfo.InvariantCulture), TimeSpan.Zero);
            var video = Guid.ParseExact(after[20..], "N");
            query = query.Where(x => x.UpdatedAtUtc < time || x.UpdatedAtUtc == time && x.VideoId.CompareTo(video) < 0);
        }
        var rows = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.VideoId).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(x => WatchHistoryCacheState.From(x).Item()).ToArray();
        return new(items, more ? cursors.Encode(user,
            $"{SubscriptionCursorCodec.SortTime(items[^1].UpdatedAtUtc)}:{items[^1].VideoId:N}") : null);
    }

    private Task HydrateAsync(Guid user, CancellationToken ct)
    {
        RedisKey[] controls = [$"{ReadyKey(user)}:lease", $"{ReadyKey(user)}:generation", ReadyKey(user)];
        return new CacheRebuilder(scripts).EnsureAsync(controls, async (token, cancel) =>
        {
            await using var strategy = await contexts.CreateDbContextAsync(cancel);
            await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await contexts.CreateDbContextAsync(cancel);
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancel);
                Guid? last = null;
                while (true)
                {
                    var query = db.WatchHistory.AsNoTracking().Where(x => x.UserId == user);
                    if (last is { } id) query = query.Where(x => x.VideoId.CompareTo(id) > 0);
                    var rows = await query.OrderBy(x => x.VideoId).Take(500).ToListAsync(cancel);
                    if (rows.Count == 0) break;
                    var args = new List<RedisValue> { token };
                    foreach (var row in rows) args.AddRange([row.VideoId.ToString("D"),
                        JsonSerializer.Serialize(WatchHistoryCacheState.From(row), WatchHistoryCacheState.Json)]);
                    if ((int)await scripts.ExecuteAsync("history-merge", [StateKey(user), OrderKey(user), controls[0], controls[1]],
                        args.ToArray(), cancel) != 1) throw new RedisServerException("CACHE_REBUILD_LOST");
                    last = rows[^1].VideoId;
                }
                await transaction.CommitAsync(cancel);
            });
        }, ct);
    }
    private static WatchHistoryCacheState Parse(RedisResult value) =>
        JsonSerializer.Deserialize<WatchHistoryCacheState>((string)value!, WatchHistoryCacheState.Json)!;
}
