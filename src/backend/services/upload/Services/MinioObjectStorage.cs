using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Implements private source-video storage using the MinIO S3-compatible client.
/// </summary>
public sealed class MinioObjectStorage(
    IMinioClient minioClient,
    IOptions<ObjectStorageOptions> options,
    ObjectMetadataFactory metadataFactory) : IObjectStorage
{
    private readonly string bucketName = options.Value.Bucket;

    /// <inheritdoc />
    public string BucketName => bucketName;

    /// <inheritdoc />
    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName),
                cancellationToken);
            if (!exists)
            {
                await minioClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(bucketName),
                    cancellationToken);
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new ObjectStorageException("MinIO bucket initialization failed.", exception);
        }
    }

    /// <inheritdoc />
    public async Task<StoredObject> UploadAsync(
        ObjectUpload upload,
        CancellationToken cancellationToken)
    {
        var headers = metadataFactory.Create(upload);

        try
        {
            var response = await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(upload.ObjectKey)
                    .WithStreamData(upload.Content)
                    .WithObjectSize(-1)
                    .WithContentType(upload.ContentType)
                    .WithHeaders(new Dictionary<string, string>(headers)),
                cancellationToken);

            return new StoredObject(bucketName, upload.ObjectKey, response.Etag.Trim('"'));
        }
        catch (UploadRequestException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new ObjectStorageException("MinIO object upload failed.", exception);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await minioClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(bucketName).WithObject(objectKey),
                cancellationToken);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new ObjectStorageException("MinIO object deletion failed.", exception);
        }
    }

    /// <inheritdoc />
    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName),
                cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException($"MinIO bucket '{bucketName}' does not exist.");
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is InvalidOperationException)
        {
            throw new ObjectStorageException("MinIO is unavailable.", exception);
        }
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is MinioException or HttpRequestException or IOException;
}
