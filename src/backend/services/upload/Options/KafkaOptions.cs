namespace StreamForge.Upload.Api.Options;

/// <summary>
/// Configures Kafka connectivity and processing-topic initialization.
/// </summary>
public sealed class KafkaOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Kafka";

    /// <summary>Gets the Kafka bootstrap-server list.</summary>
    public string BootstrapServers { get; init; } = string.Empty;

    /// <summary>Gets the destination processing topic name.</summary>
    public string TopicName { get; init; } = "video-processing";

    /// <summary>Gets the partition count used only when creating a missing topic.</summary>
    public int PartitionCount { get; init; } = 1;

    /// <summary>Gets the replication factor used only when creating a missing topic.</summary>
    public short ReplicationFactor { get; init; } = 1;

    /// <summary>Gets the startup topic-operation timeout in seconds.</summary>
    public int InitializationTimeoutSeconds { get; init; } = 60;
}
