namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class VideoView
{
    public Guid VideoId { get; set; }
    public long Count { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public KnownVideo Video { get; set; } = null!;
}
