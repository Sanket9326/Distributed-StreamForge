namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class Comment
{
    public Guid Id { get; set; }
    public Guid VideoId { get; set; }
    public Guid UserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public KnownVideo Video { get; set; } = null!;
}
