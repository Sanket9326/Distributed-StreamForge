namespace StreamForge.Playback.Api.Options;
public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";
    public int SignedUrlExpirySeconds { get; init; } = 3600;
}
public sealed class StorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string Endpoint { get; init; } = string.Empty;
    public string PublicEndpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string RenditionsBucket { get; init; } = "streamforge-renditions";
    public bool UseSsl { get; init; }
    public bool PublicUseSsl { get; init; }
}
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    public string BootstrapServers { get; init; } = string.Empty;
    public string ConsumerGroupId { get; init; } = "streamforge-playback-v1";
    public string CompletedTopic { get; init; } = "video-transcoding-completed";
}
