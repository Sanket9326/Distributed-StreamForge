namespace StreamForge.Upload.Api.Options;

public sealed class UploadStorageOptions
{
    public const string SectionName = "UploadStorage";
    public const long DefaultMaxFileSizeBytes = 1_073_741_824;

    public string RootPath { get; init; } = string.Empty;

    public long MaxFileSizeBytes { get; init; } = DefaultMaxFileSizeBytes;
}
