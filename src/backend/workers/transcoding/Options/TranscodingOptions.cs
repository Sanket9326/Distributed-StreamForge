namespace StreamForge.Transcoding.Worker.Options;

/// <summary>Configures job leasing, retries, concurrency, and scratch storage.</summary>
public sealed class TranscodingOptions
{
    public const string SectionName = "Transcoding";

    public int MaxConcurrentJobs { get; init; } = 1;

    public int MaxAttempts { get; init; } = 5;

    public int PollIntervalMilliseconds { get; init; } = 1_000;

    public int LeaseDurationSeconds { get; init; } = 120;

    public int LeaseHeartbeatSeconds { get; init; } = 30;

    public int RetryBaseDelaySeconds { get; init; } = 30;

    public int RetryMaximumDelaySeconds { get; init; } = 900;

    public int JobTimeoutSeconds { get; init; } = 21_600;

    public string ScratchPath { get; init; } = "/tmp/streamforge-transcoding";

    public long MinimumFreeScratchBytes { get; init; } = 2_147_483_648;
}
