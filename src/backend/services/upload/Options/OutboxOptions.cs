namespace StreamForge.Upload.Api.Options;

/// <summary>
/// Configures outbox polling, batching, retry delay, and health thresholds.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Outbox";

    /// <summary>Gets the idle polling interval in milliseconds.</summary>
    public int PollIntervalMilliseconds { get; init; } = 1_000;

    /// <summary>Gets the maximum rows claimed in one publishing batch.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Gets the upper bound for exponential retry delays.</summary>
    public int MaximumRetryDelaySeconds { get; init; } = 60;

    /// <summary>Gets the pending-event age that degrades outbox health.</summary>
    public int DegradedAfterSeconds { get; init; } = 300;
}
