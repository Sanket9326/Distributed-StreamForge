using Microsoft.AspNetCore.Http;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Validates the filename extension and declared media type of an uploaded source video.
/// </summary>
public sealed class VideoFileValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mov",
        ".mp4",
        ".webm"
    };

    /// <summary>Validates a submitted filename and content type.</summary>
    /// <param name="fileName">The untrusted client filename.</param>
    /// <param name="contentType">The media type declared for the multipart file section.</param>
    /// <returns>The validated lowercase file extension.</returns>
    public string Validate(string fileName, string? contentType)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new UploadRequestException(
                StatusCodes.Status400BadRequest,
                "Invalid file",
                "The uploaded file must have a filename.");
        }

        var extension = Path.GetExtension(safeFileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new UploadRequestException(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported video format",
                "Supported video extensions are .mp4, .mov, .webm, and .mkv.");
        }

        if (string.IsNullOrWhiteSpace(contentType) ||
            !contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadRequestException(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported media type",
                "The uploaded file must declare a video content type.");
        }

        return extension.ToLowerInvariant();
    }
}
