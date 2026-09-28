using System.ComponentModel.DataAnnotations;

namespace StreamForge.Engagement.Api.Models;

public sealed record SaveWatchProgress([Required] long? PositionMs,
    [Required] long? DurationMs, [Required] bool? IsCompleted)
{
    public bool IsValid => PositionMs is >= 0 && DurationMs is > 0 and <= 9_007_199_254_740_991 &&
        PositionMs <= DurationMs && IsCompleted.HasValue;
}
public sealed record WatchHistoryItem(Guid VideoId, long PositionMs, long DurationMs, bool IsCompleted,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int SourcePartition, string SourceOffset);
public sealed record WatchHistoryPage(IReadOnlyList<WatchHistoryItem> Items, string? NextCursor);
public sealed record WatchHistoryMutation(WatchHistoryItem Progress, bool CachePending);
public sealed record UserWatchProgressSavedV1(Guid EventId, string EventType, int EventVersion,
    DateTimeOffset OccurredAtUtc, Guid UserId, Guid VideoId, long PositionMs, long DurationMs,
    bool IsCompleted, string CorrelationId)
{
    public const string Type = "user.watch-progress.saved.v1";
    public string Key => $"{UserId:D}:{VideoId:D}";
}
public sealed record WatchHistoryHeader(string Key, byte[]? Value);
// Origin is immutable across republication; transport offsets are deliberately separate.
public sealed record WatchHistoryRetryEnvelope(Guid EventId, string Payload, WatchHistoryHeader[] Headers,
    string Key, string DestinationTopic, int OriginalPartition, string OriginalOffset, int Attempt,
    long DueAtUnixMs, DateTimeOffset FirstFailedAtUtc, string FailureCategory)
{
    public string RetryId => $"{EventId:D}:{Attempt}";
}
public sealed record WatchHistoryDeadLetterV1(Guid EventId, string EventType, int EventVersion,
    DateTimeOffset OccurredAtUtc, string SourceTopic, int SourcePartition, string SourceOffset,
    string? SourceKey, string Reason, string Payload, WatchHistoryRetryEnvelope? Retry)
{
    public const string Type = "user.watch-history.dead-letter.v1";
}
