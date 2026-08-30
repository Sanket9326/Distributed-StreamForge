namespace StreamForge.Transcoding.Worker.Options;

/// <summary>Configures MinIO source access and rendition storage.</summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Endpoint { get; init; } = string.Empty;

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public string RenditionsBucket { get; init; } = "streamforge-renditions";

    public bool UseSsl { get; init; }
}
