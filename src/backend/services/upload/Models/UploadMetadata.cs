namespace StreamForge.Upload.Api.Models;

/// <summary>
/// Contains validated and normalized descriptive metadata for a video submission.
/// </summary>
public sealed record UploadMetadata(
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags);
