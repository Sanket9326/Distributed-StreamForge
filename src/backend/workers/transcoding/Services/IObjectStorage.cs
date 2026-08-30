namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Defines source-read and rendition-write MinIO operations.</summary>
public interface IObjectStorage
{
    string RenditionsBucket { get; }

    Task EnsureRenditionsBucketAsync(CancellationToken cancellationToken);

    Task<StoredObjectInfo> StatAsync(string bucket, string objectKey, CancellationToken cancellationToken);

    Task DownloadAsync(string bucket, string objectKey, string destinationPath, CancellationToken cancellationToken);

    Task<StoredObjectInfo> UploadRenditionAsync(
        string objectKey,
        string filePath,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);

    Task DeleteRenditionAsync(string objectKey, CancellationToken cancellationToken);

    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}

public sealed record StoredObjectInfo(string Bucket, string ObjectKey, string Etag, long SizeBytes, string ContentType);
