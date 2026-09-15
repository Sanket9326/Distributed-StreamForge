using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Infrastructure.Redis;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class SubscriptionCache(LuaScriptExecutor scripts, IDbContextFactory<EngagementDbContext> contexts,
    SubscriptionCursorCodec cursors)
{
    private const string Prefix = "streamforge:engagement:subscriptions:v1";
    public static RedisKey StateKey(Guid id, bool incoming) => $"{Prefix}:user:{id:D}:{(incoming ? "subscribers" : "following")}:state";
    public static RedisKey OrderKey(Guid id, bool incoming) => $"{Prefix}:user:{id:D}:{(incoming ? "subscribers" : "following")}:order";
    public static RedisKey ReadyKey(Guid id, bool incoming) => $"{Prefix}:user:{id:D}:{(incoming ? "subscribers" : "following")}:ready";

    public async Task<SubscriptionCacheState> ApplyAsync(SubscriptionCacheState state, CancellationToken ct)
    {
        var result = await scripts.ExecuteAsync("subscription-apply",
            [StateKey(state.SubscriberId, false), OrderKey(state.SubscriberId, false),
             StateKey(state.CreatorId, true), OrderKey(state.CreatorId, true)],
            [state.CreatorId.ToString("D"), state.SubscriberId.ToString("D"), JsonSerializer.Serialize(state, SubscriptionCacheState.Json)], ct);
        return Parse(result);
    }

    public async Task<IReadOnlyList<SubscriptionStatus>> GetStatusAsync(Guid userId, Guid[] ids, CancellationToken ct)
    {
        var values = await ReadStatusesAsync(userId, ids, ct);
        var missing = ids.Where((_, i) => values[i].IsNull).ToArray();
        if (missing.Length > 0)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var rows = await db.Subscriptions.AsNoTracking()
                .Where(x => x.SubscriberId == userId && missing.Contains(x.CreatorId)).ToListAsync(ct);
            foreach (var row in rows) await ApplyAsync(SubscriptionCacheState.From(row), ct);
            // Re-read after PostgreSQL to observe concurrent accepted changes, including previously absent pairs.
            values = await ReadStatusesAsync(userId, ids, ct);
        }
        return ids.Select((id, i) => values[i].IsNull ? new SubscriptionStatus(id, false, null, null) :
            Status(id, Parse(values[i]))).ToArray();
    }

    private async Task<RedisResult[]> ReadStatusesAsync(Guid user, Guid[] ids, CancellationToken ct) =>
        (RedisResult[])(await scripts.ExecuteAsync("subscription-status", [StateKey(user, false)],
            ids.Select(x => (RedisValue)x.ToString("D")).ToArray(), ct))!;

    public async Task<SubscriptionPage> GetPageAsync(Guid user, bool incoming, int limit, string? cursor, CancellationToken ct)
    {
        var after = cursors.Decode(user, incoming, cursor);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await HydrateAsync(user, incoming, ct);
            var result = (RedisResult[])(await scripts.ExecuteAsync("subscription-page",
                [StateKey(user, incoming), OrderKey(user, incoming), ReadyKey(user, incoming)], [limit, after ?? ""], ct))!;
            if ((int)result[0] != 1) continue;
            var items = ((RedisResult[])result[2])!.Select(entry =>
            {
                var pair = (RedisResult[])entry!;
                var state = Parse(pair[1]);
                return new SubscriptionItem(Guid.Parse((string)pair[0]!), state.CreatedAtUtc, state.SourcePartition, state.SourceOffset);
            }).ToArray();
            return new(items, result[1].IsNull ? null : cursors.Encode(user, incoming, (string)result[1]!));
        }
        throw new RedisServerException("CACHE_REBUILD_LOST");
    }

    public async Task<SubscriptionPage> GetDatabasePageAsync(Guid user, bool incoming, int limit, string? cursor, CancellationToken ct)
    {
        var after = cursors.Decode(user, incoming, cursor);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var query = db.Subscriptions.AsNoTracking().Where(x => x.IsActive && (incoming ? x.CreatorId == user : x.SubscriberId == user));
        if (after is not null)
        {
            var time = new DateTimeOffset(long.Parse(after[..19], CultureInfo.InvariantCulture), TimeSpan.Zero);
            var counterpart = Guid.ParseExact(after[20..], "N");
            query = query.Where(x => x.CreatedAtUtc < time || x.CreatedAtUtc == time &&
                (incoming ? x.SubscriberId.CompareTo(counterpart) < 0 : x.CreatorId.CompareTo(counterpart) < 0));
        }
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => incoming ? x.SubscriberId : x.CreatorId).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(x => new SubscriptionItem(incoming ? x.SubscriberId : x.CreatorId, x.CreatedAtUtc,
            x.SourcePartition, x.SourceOffset.ToString(CultureInfo.InvariantCulture))).ToArray();
        return new(items, hasMore ? cursors.Encode(user, incoming,
            $"{SubscriptionCursorCodec.SortTime(items[^1].CreatedAtUtc)}:{items[^1].UserId:N}") : null);
    }

    private Task HydrateAsync(Guid user, bool incoming, CancellationToken ct)
    {
        RedisKey[] controls = [$"{ReadyKey(user, incoming)}:lease", $"{ReadyKey(user, incoming)}:generation", ReadyKey(user, incoming)];
        return new CacheRebuilder(scripts).EnsureAsync(controls, async (token, cancel) =>
        {
            await using var strategyDb = await contexts.CreateDbContextAsync(cancel);
            await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await contexts.CreateDbContextAsync(cancel);
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancel);
                Guid? last = null;
                while (true)
                {
                    var query = db.Subscriptions.AsNoTracking().Where(x => incoming ? x.CreatorId == user : x.SubscriberId == user);
                    if (last is { } id)
                        query = query.Where(x => incoming ? x.SubscriberId.CompareTo(id) > 0 : x.CreatorId.CompareTo(id) > 0);
                    var rows = await query.OrderBy(x => incoming ? x.SubscriberId : x.CreatorId).Take(500).ToListAsync(cancel);
                    if (rows.Count == 0) break;
                    var args = new List<RedisValue> { token };
                    foreach (var row in rows)
                        args.AddRange([(incoming ? row.SubscriberId : row.CreatorId).ToString("D"),
                            JsonSerializer.Serialize(SubscriptionCacheState.From(row), SubscriptionCacheState.Json)]);
                    if ((int)await scripts.ExecuteAsync("subscription-merge",
                        [StateKey(user, incoming), OrderKey(user, incoming), controls[0], controls[1]], args.ToArray(), cancel) != 1)
                        throw new RedisServerException("CACHE_REBUILD_LOST");
                    last = incoming ? rows[^1].SubscriberId : rows[^1].CreatorId;
                }
                await transaction.CommitAsync(cancel);
            });
        }, ct);
    }

    private static SubscriptionCacheState Parse(RedisResult value) =>
        JsonSerializer.Deserialize<SubscriptionCacheState>((string)value!, SubscriptionCacheState.Json)!;
    private static SubscriptionStatus Status(Guid id, SubscriptionCacheState state) =>
        new(id, state.IsActive, state.SourcePartition, state.SourceOffset);
}
