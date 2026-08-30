namespace StreamForge.Transcoding.Worker.Options;

/// <summary>Configures Kafka intake and outcome topics.</summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = string.Empty;

    public string ConsumerGroupId { get; init; } = "streamforge-transcoding-v1";

    public string InputTopic { get; init; } = "video-processing";

    public string CompletedTopic { get; init; } = "video-transcoding-completed";

    public string FailedTopic { get; init; } = "video-transcoding-failed";

    public string DeadLetterTopic { get; init; } = "video-processing-dead-letter";

    public int PartitionCount { get; init; } = 1;

    public short ReplicationFactor { get; init; } = 1;

    public int InitializationTimeoutSeconds { get; init; } = 60;
}
