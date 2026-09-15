namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class Reaction
{
    public Guid VideoId { get; set; }
    public Guid UserId { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int SourcePartition { get; set; }
    public long SourceOffset { get; set; }
    public KnownVideo Video { get; set; } = null!;
}
