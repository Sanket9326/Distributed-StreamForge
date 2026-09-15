namespace StreamForge.Engagement.Api.Data.Entities;

public sealed class ConsumedKafkaMessage
{
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public Guid EventId { get; set; }
    public DateTimeOffset ConsumedAtUtc { get; set; }
    public string? RejectionCode { get; set; }
}
