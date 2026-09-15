using StackExchange.Redis;

namespace StreamForge.Engagement.Api.Infrastructure.Redis;

/// <summary>Fences bounded cache hydration against other replicas, expired leases, and Redis restarts.</summary>
public sealed class CacheRebuilder(LuaScriptExecutor scripts)
{
    public async Task EnsureAsync(RedisKey[] controlKeys, Func<string, CancellationToken, Task> merge,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(35));
        while (true)
        {
            var acquired = (int)await scripts.ExecuteAsync("begin-rebuild", controlKeys, [token], wait.Token);
            if (acquired == 2) return;
            if (acquired == 1) break;
            await Task.Delay(100, wait.Token);
        }
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RenewAsync(controlKeys, token, work);
        try
        {
            await merge(token, work.Token);
            work.Cancel();
            await heartbeat;
            if ((int)await scripts.ExecuteAsync("rebuild-control", controlKeys, [token, "complete"], cancellationToken) != 1)
                throw new RedisServerException("CACHE_REBUILD_LOST");
        }
        finally
        {
            work.Cancel();
            try { await heartbeat; } catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            try { await scripts.ExecuteAsync("rebuild-control", controlKeys, [token, "release"], CancellationToken.None); }
            catch (RedisException) { }
        }
    }

    private async Task RenewAsync(RedisKey[] keys, string token, CancellationTokenSource work)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(work.Token))
                if ((int)await scripts.ExecuteAsync("rebuild-control", keys, [token, "renew"], work.Token) != 1)
                {
                    work.Cancel();
                    throw new RedisServerException("CACHE_REBUILD_LOST");
                }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch { work.Cancel(); throw; }
    }
}
