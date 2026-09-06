namespace StreamForge.Engagement.Api.Models;

public sealed record VideoReactionChangedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid VideoId,
    Guid UserId,
    string Reaction,
    string CorrelationId)
{
    public const string Type = "video.reaction.changed.v1";
}

public sealed record VideoViewQualifiedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid VideoId,
    string CorrelationId)
{
    public const string Type = "video.view.qualified.v1";
}

public sealed record VideoCompletedEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid VideoId);
