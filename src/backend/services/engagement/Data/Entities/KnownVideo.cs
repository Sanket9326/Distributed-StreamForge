namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class KnownVideo
{
    public Guid VideoId { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
}
