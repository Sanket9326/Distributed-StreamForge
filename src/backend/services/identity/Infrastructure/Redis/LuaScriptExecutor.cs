using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using StackExchange.Redis;

namespace StreamForge.Identity.Api.Infrastructure.Redis;

/// <summary>Executes embedded scripts without exposing keys, credentials, or arguments in logs.</summary>
public sealed class LuaScriptExecutor(IConnectionMultiplexer redis, ILogger<LuaScriptExecutor>? logger = null)
{
    private static readonly ConcurrentDictionary<string, string> Sources = new();
    private static readonly Meter Meter = new("StreamForge.Identity.Redis");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("redis.script.duration", "ms");

    /// <summary>Executes a packaged logical operation with safe diagnostics and script-cache recovery.</summary>
    public async Task<RedisResult> ExecuteAsync(string name, RedisKey[] keys, RedisValue[] values,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            // Passing source lets StackExchange.Redis recover its EVALSHA cache after NOSCRIPT.
            return await redis.GetDatabase().ScriptEvaluateAsync(Sources.GetOrAdd(name, Load), keys, values)
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            outcome = exception.GetType().Name;
            logger?.LogWarning("Redis script {Script} failed with {Category}", name, exception.GetType().Name);
            throw;
        }
        finally
        {
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("script", name), new KeyValuePair<string, object?>("outcome", outcome));
            logger?.LogDebug("Redis script {Script} took {Milliseconds} ms", name,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>Checks Redis availability using a read-only script without accessing session data.</summary>
    public async Task ReadyAsync(CancellationToken cancellationToken) =>
        _ = await ExecuteAsync("ready", [], [], cancellationToken);

    private static string Load(string name)
    {
        var assembly = typeof(LuaScriptExecutor).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith($".Scripts.{name}.lua", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var source = reader.ReadToEnd();
        return source.Contains("-- @common", StringComparison.Ordinal)
            ? source.Replace("-- @common", Sources.GetOrAdd("common", Load), StringComparison.Ordinal)
            : source;
    }
}
