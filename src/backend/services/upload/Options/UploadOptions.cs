namespace StreamForge.Upload.Api.Options;

/// <summary>
/// Configures request-level video upload limits.
/// </summary>
public sealed class UploadOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Upload";

    /// <summary>Gets the default one-gibibyte source-video limit.</summary>
    public const long DefaultMaxFileSizeBytes = 1_073_741_824;

    /// <summary>Gets the maximum accepted source-video size in bytes.</summary>
    public long MaxFileSizeBytes { get; init; } = DefaultMaxFileSizeBytes;
}
