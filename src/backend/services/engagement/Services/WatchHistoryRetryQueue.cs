using System.Text.Json;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Infrastructure.Redis;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryRetryQueue(LuaScriptExecutor scripts, TimeProvider clock)
{
    public const int MaximumRetries = 8;
    public static readonly RedisKey[] Keys = ["streamforge:engagement:history:retry:v1:due",
        "streamforge:engagement:history:retry:v1:inflight", "streamforge:engagement:history:retry:v1:members",
        "streamforge:engagement:history:retry:v1:leases", "streamforge:engagement:history:retry:v1:completed"];
    public static TimeSpan Delay(int attempt, double sample) =>
        TimeSpan.FromSeconds(Math.Clamp(sample, 0, 1) * Math.Min(60, 2 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 8))));
    public async Task EnqueueAsync(WatchHistoryRetryEnvelope envelope, CancellationToken ct) =>
        _ = await scripts.ExecuteAsync("history-retry-enqueue", [Keys[0], Keys[2]],
            [envelope.RetryId, JsonSerializer.Serialize(envelope, WatchHistoryCacheState.Json), envelope.DueAtUnixMs], ct);
    public async Task<IReadOnlyList<WatchHistoryRetryEnvelope>> ClaimAsync(string token, CancellationToken ct)
    {
        var result = (RedisResult[])(await scripts.ExecuteAsync("history-retry-claim", Keys,
            [clock.GetUtcNow().ToUnixTimeMilliseconds(), token], ct))!;
        return result.Select(x => JsonSerializer.Deserialize<WatchHistoryRetryEnvelope>((string)x!, WatchHistoryCacheState.Json)!).ToArray();
    }
    public async Task<bool> ControlAsync(string id, string token, string action, CancellationToken ct) =>
        (int)await scripts.ExecuteAsync("history-retry-control", Keys,
            [id, token, action, clock.GetUtcNow().ToUnixTimeMilliseconds(), (long)Delay(5, Random.Shared.NextDouble()).TotalMilliseconds], ct) == 1;
    public async Task<(long Depth, double OverdueSeconds)> StatsAsync(CancellationToken ct)
    {
        var result = (RedisResult[])(await scripts.ExecuteAsync("history-retry-stats", [Keys[0], Keys[1]], [], ct))!;
        return ((long)result[0] + (long)result[1], result[2].IsNull ? 0 : Math.Max(0,
            (clock.GetUtcNow().ToUnixTimeMilliseconds() - (double)result[2]) / 1000));
    }
}
