using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Reads immutable sources and writes private renditions using the MinIO client.</summary>
public sealed class MinioObjectStorage(
    IMinioClient minioClient,
    IOptions<ObjectStorageOptions> options) : IObjectStorage
{
    public string RenditionsBucket { get; } = options.Value.RenditionsBucket;

    public async Task EnsureRenditionsBucketAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(RenditionsBucket),
                cancellationToken);
            if (!exists)
            {
                await minioClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(RenditionsBucket),
                    cancellationToken);
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageFailure("Rendition bucket initialization failed.", exception);
        }
    }

    public async Task<StoredObjectInfo> StatAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await minioClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(objectKey),
                cancellationToken);
            return new StoredObjectInfo(bucket, objectKey, NormalizeEtag(result.ETag), result.Size, result.ContentType);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageFailure("MinIO object metadata could not be read.", exception);
        }
    }

    public async Task DownloadAsync(
        string bucket,
        string objectKey,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await minioClient.GetObjectAsync(
                new GetObjectArgs().WithBucket(bucket).WithObject(objectKey).WithFile(destinationPath),
                cancellationToken);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageFailure("Source object download failed.", exception);
        }
    }

    public async Task<StoredObjectInfo> UploadRenditionAsync(
        string objectKey,
        string filePath,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var headers = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
            var response = await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(RenditionsBucket)
                    .WithObject(objectKey)
                    .WithFileName(filePath)
                    .WithContentType("video/mp4")
                    .WithHeaders(headers),
                cancellationToken);
            var info = new FileInfo(filePath);
            return new StoredObjectInfo(
                RenditionsBucket,
                objectKey,
                NormalizeEtag(response.Etag),
                info.Length,
                "video/mp4");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageFailure("Rendition upload failed.", exception);
        }
    }

    public async Task DeleteRenditionAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await minioClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(RenditionsBucket).WithObject(objectKey),
                cancellationToken);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageFailure("Rendition deletion failed.", exception);
        }
    }

    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(RenditionsBucket),
                cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException($"Rendition bucket '{RenditionsBucket}' does not exist.");
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is InvalidOperationException)
        {
            throw StorageFailure("MinIO rendition storage is unavailable.", exception);
        }
    }

    private static TransientTranscodingException StorageFailure(string message, Exception exception) =>
        new("object_storage_unavailable", message, exception);

    private static string NormalizeEtag(string etag) => etag.Trim('"');

    private static bool IsStorageFailure(Exception exception) =>
        exception is MinioException or HttpRequestException or IOException;
}
