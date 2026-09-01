namespace StreamForge.Feed.Api.Services;

public sealed record ConsumedEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload);

public sealed record ProjectionResult(
    bool CompletionRecorded,
    Guid VideoId,
    DateTimeOffset AvailableAtUtc)
{
    public static ProjectionResult None { get; } = new(false, Guid.Empty, default);

    public static ProjectionResult Completed(Guid videoId, DateTimeOffset availableAtUtc) =>
        new(true, videoId, availableAtUtc);
}

public interface IFeedEventProjector
{
    Task<ProjectionResult> ProjectAsync(ConsumedEnvelope envelope, CancellationToken cancellationToken);
}
