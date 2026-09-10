namespace StreamForge.Feed.Api.Options;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = string.Empty;

    public string ConsumerGroupId { get; init; } = "streamforge-feed-v1";

    public string UploadedTopic { get; init; } = "video-processing";

    public string CompletedTopic { get; init; } = "video-transcoding-completed";

    public string SearchIndexTopic { get; init; } = "video-search-index";

    public int PartitionCount { get; init; } = 1;

    public short ReplicationFactor { get; init; } = 1;

    public int InitializationTimeoutSeconds { get; init; } = 60;
}
