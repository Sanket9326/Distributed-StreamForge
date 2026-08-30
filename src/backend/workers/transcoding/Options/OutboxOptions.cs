namespace StreamForge.Transcoding.Worker.Options;

/// <summary>Configures durable outcome publication.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalMilliseconds { get; init; } = 1_000;

    public int BatchSize { get; init; } = 20;

    public int MaximumRetryDelaySeconds { get; init; } = 60;
}
