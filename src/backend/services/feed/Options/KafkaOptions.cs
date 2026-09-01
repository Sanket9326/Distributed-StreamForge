namespace StreamForge.Feed.Api.Options;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = string.Empty;

    public string ConsumerGroupId { get; init; } = "streamforge-feed-v1";

    public string UploadedTopic { get; init; } = "video-processing";

    public string CompletedTopic { get; init; } = "video-transcoding-completed";

    public int InitializationTimeoutSeconds { get; init; } = 60;
}
