namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Defines the object-storage operations required by video ingestion and health checks.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Gets the private source-video bucket name.</summary>
    string BucketName { get; }

    /// <summary>Creates the source bucket when it does not already exist.</summary>
    /// <param name="cancellationToken">Signals that initialization should stop.</param>
    Task EnsureBucketAsync(CancellationToken cancellationToken);

    /// <summary>Streams one source video into object storage.</summary>
    /// <param name="upload">The object coordinates, metadata, and content stream.</param>
    /// <param name="cancellationToken">Signals that the upload should stop.</param>
    /// <returns>The durable object coordinates and ETag.</returns>
    Task<StoredObject> UploadAsync(ObjectUpload upload, CancellationToken cancellationToken);

    /// <summary>Deletes an object during failed-ingestion compensation.</summary>
    /// <param name="objectKey">The object key to delete from the source bucket.</param>
    /// <param name="cancellationToken">Signals that deletion should stop.</param>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>Verifies that storage and the configured bucket are available.</summary>
    /// <param name="cancellationToken">Signals that the probe should stop.</param>
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Describes a source-video object and the stream that will be stored.
/// </summary>
public sealed record ObjectUpload(
    Guid VideoId,
    string ObjectKey,
    string OriginalFileName,
    string ContentType,
    DateTimeOffset UploadedAtUtc,
    string CorrelationId,
    Guid? OwnerId,
    Stream Content);

/// <summary>
/// Identifies an object that MinIO has stored successfully.
/// </summary>
public sealed record StoredObject(string Bucket, string ObjectKey, string Etag);
