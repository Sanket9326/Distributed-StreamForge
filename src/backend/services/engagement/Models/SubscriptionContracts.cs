namespace StreamForge.Engagement.Api.Models;

public sealed record UserSubscriptionChangedV1(Guid EventId, string EventType, int EventVersion,
    DateTimeOffset OccurredAtUtc, Guid SubscriberId, Guid CreatorId, Guid ActorId, bool IsActive, string CorrelationId)
{
    public const string Type = "user.subscription.changed.v1";
    public string Key => $"{SubscriberId:D}:{CreatorId:D}";
}
public sealed record SubscriptionDeadLetterV1(Guid EventId, string EventType, int EventVersion,
    DateTimeOffset OccurredAtUtc, string SourceTopic, int SourcePartition, long SourceOffset,
    string? SourceKey, string Reason, string Payload)
{
    public const string Type = "user.subscription.dead-letter.v1";
}
public sealed record SubscriptionItem(Guid UserId, DateTimeOffset CreatedAtUtc, int SourcePartition, string SourceOffset);
public sealed record SubscriptionPage(IReadOnlyList<SubscriptionItem> Items, string? NextCursor);
public sealed record SubscriptionStatus(Guid CreatorId, bool IsActive, int? SourcePartition, string? SourceOffset);
public sealed record SubscriptionMutation(Guid SubscriberId, Guid CreatorId, bool IsActive, bool CachePending,
    int SourcePartition, string SourceOffset);
