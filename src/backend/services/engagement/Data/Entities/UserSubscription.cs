namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class UserSubscription
{
    public Guid SubscriberId { get; set; }
    public Guid CreatorId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int SourcePartition { get; set; }
    public long SourceOffset { get; set; }
}
