namespace StreamForge.Search.Api.Options;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = string.Empty;
    public string ConsumerGroupId { get; init; } = "streamforge-search-index-v1";
    public string InputTopic { get; init; } = "video-search-index";
    public string DeadLetterTopic { get; init; } = "video-search-index-dead-letter";
    public int PartitionCount { get; init; } = 1;
    public short ReplicationFactor { get; init; } = 1;
    public int InitializationTimeoutSeconds { get; init; } = 60;
    public int MaximumRetryDelaySeconds { get; init; } = 30;
}
