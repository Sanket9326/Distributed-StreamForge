namespace StreamForge.Upload.Api.Options;

/// <summary>
/// Configures the MinIO endpoint, credentials, and private source bucket.
/// </summary>
public sealed class ObjectStorageOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ObjectStorage";

    /// <summary>Gets the MinIO host and port.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Gets the MinIO access key.</summary>
    public string AccessKey { get; init; } = string.Empty;

    /// <summary>Gets the MinIO secret key.</summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>Gets the private source-video bucket name.</summary>
    public string Bucket { get; init; } = "streamforge-videos";

    /// <summary>Gets whether the MinIO client uses TLS.</summary>
    public bool UseSsl { get; init; }
}
