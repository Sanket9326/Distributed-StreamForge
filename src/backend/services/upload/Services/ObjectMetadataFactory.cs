using System.Globalization;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Creates the safe technical metadata attached to a MinIO source object.
/// </summary>
public sealed class ObjectMetadataFactory
{
    /// <summary>Builds object metadata without placing descriptive video metadata in MinIO.</summary>
    /// <param name="upload">The upload whose technical metadata is required.</param>
    /// <returns>Headers suitable for the MinIO upload request.</returns>
    public IReadOnlyDictionary<string, string> Create(ObjectUpload upload)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-amz-meta-video-id"] = upload.VideoId.ToString("D"),
            ["x-amz-meta-uploaded-at-utc"] = upload.UploadedAtUtc
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            ["x-amz-meta-correlation-id"] = upload.CorrelationId,
            ["x-amz-meta-original-file-name"] = Uri.EscapeDataString(upload.OriginalFileName)
        };

        if (upload.OwnerId is not null)
        {
            metadata["x-amz-meta-owner-id"] = upload.OwnerId.Value.ToString("D");
        }

        return metadata;
    }
}
