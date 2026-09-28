namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class WatchHistory
{
    public Guid UserId { get; set; }
    public Guid VideoId { get; set; }
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int SourcePartition { get; set; }
    public long SourceOffset { get; set; }
}
