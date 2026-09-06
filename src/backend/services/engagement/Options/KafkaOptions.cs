namespace StreamForge.Engagement.Api.Options;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    public string BootstrapServers { get; init; } = string.Empty;
    public string ReactionTopic { get; init; } = "video-engagement-reactions";
    public string ViewTopic { get; init; } = "video-engagement-views";
    public string CompletedTopic { get; init; } = "video-transcoding-completed";
    public string ReactionConsumerGroupId { get; init; } = "streamforge-engagement-reactions-v1";
    public string ViewConsumerGroupId { get; init; } = "streamforge-engagement-views-v1";
    public string CatalogConsumerGroupId { get; init; } = "streamforge-engagement-catalog-v1";
    public int PartitionCount { get; init; } = 1;
    public short ReplicationFactor { get; init; } = 1;
    public int InitializationTimeoutSeconds { get; init; } = 60;
}
