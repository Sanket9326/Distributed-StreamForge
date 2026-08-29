using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using StreamForge.Upload.Api.Data.Entities;
using StreamForge.Upload.Api.Models;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Coordinates multipart parsing, validation, MinIO storage, and PostgreSQL video/outbox persistence.
/// </summary>
public sealed class VideoIngestionService(
    VideoFileValidator fileValidator,
    UploadMetadataValidator metadataValidator,
    ObjectKeyFactory objectKeyFactory,
    IObjectStorage objectStorage,
    VideoSubmissionStore submissionStore,
    IOptions<UploadOptions> uploadOptions,
    IOptions<KafkaOptions> kafkaOptions,
    TimeProvider timeProvider,
    ILogger<VideoIngestionService> logger) : IVideoIngestionService
{
    private const string FileFieldName = "file";
    private const string TitleFieldName = "title";
    private const string DescriptionFieldName = "description";
    private const string HashtagsFieldName = "hashtags";
    private const int MaximumBoundaryLength = 128;
    private readonly long maxFileSizeBytes = uploadOptions.Value.MaxFileSizeBytes;
    private readonly string topicName = kafkaOptions.Value.TopicName;

    /// <inheritdoc />
    public async Task<UploadResponse> IngestAsync(
        Stream requestBody,
        string? requestContentType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var videoId = Guid.NewGuid();
        var uploadedAtUtc = timeProvider.GetUtcNow();
        string? objectKey = null;
        var objectStored = false;
        var submissionCommitted = false;

        try
        {
            var boundary = GetBoundary(requestContentType);
            var reader = new MultipartReader(boundary, requestBody);
            string? title = null;
            string? description = null;
            var hashtags = new List<string>();
            var titleSeen = false;
            var descriptionSeen = false;
            string? originalFileName = null;
            string? fileContentType = null;
            long sizeBytes = 0;
            StoredObject? storedObject = null;

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !string.Equals(
                        disposition.DispositionType.Value,
                        "form-data",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fieldName = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                var submittedFileName = HeaderUtilities.RemoveQuotes(
                    disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;

                if (!string.IsNullOrWhiteSpace(submittedFileName))
                {
                    if (!string.Equals(fieldName, FileFieldName, StringComparison.Ordinal))
                    {
                        throw InvalidRequest(
                            "Invalid file field",
                            $"The video must be supplied in the '{FileFieldName}' form field.");
                    }

                    if (storedObject is not null)
                    {
                        throw InvalidRequest("Too many files", "Only one video can be uploaded per request.");
                    }

                    originalFileName = Path.GetFileName(submittedFileName);
                    if (originalFileName.Length > 255)
                    {
                        throw InvalidRequest(
                            "Invalid file name",
                            "The video filename cannot exceed 255 characters.");
                    }

                    fileContentType = section.ContentType;
                    var extension = fileValidator.Validate(originalFileName, fileContentType);
                    objectKey = objectKeyFactory.Create(videoId, uploadedAtUtc, extension);

                    await using var limitedStream = new SizeLimitedReadStream(
                        section.Body,
                        maxFileSizeBytes);
                    storedObject = await objectStorage.UploadAsync(
                        new ObjectUpload(
                            videoId,
                            objectKey,
                            originalFileName,
                            fileContentType!,
                            uploadedAtUtc,
                            correlationId,
                            OwnerId: null,
                            limitedStream),
                        cancellationToken);
                    objectStored = true;
                    sizeBytes = limitedStream.BytesRead;

                    if (sizeBytes == 0)
                    {
                        throw InvalidRequest("Empty file", "The uploaded video must not be empty.");
                    }

                    continue;
                }

                switch (fieldName)
                {
                    case TitleFieldName when !titleSeen:
                        titleSeen = true;
                        title = await ReadTextFieldAsync(
                            section.Body,
                            UploadMetadataValidator.MaximumTitleLength,
                            cancellationToken);
                        break;
                    case DescriptionFieldName when !descriptionSeen:
                        descriptionSeen = true;
                        description = await ReadTextFieldAsync(
                            section.Body,
                            UploadMetadataValidator.MaximumDescriptionLength,
                            cancellationToken);
                        break;
                    case HashtagsFieldName:
                        hashtags.Add(await ReadTextFieldAsync(
                            section.Body,
                            UploadMetadataValidator.MaximumHashtagLength + 1,
                            cancellationToken));
                        break;
                    case TitleFieldName or DescriptionFieldName:
                        throw InvalidRequest(
                            "Duplicate metadata field",
                            $"The '{fieldName}' field may be supplied only once.");
                    default:
                        throw InvalidRequest(
                            "Unknown metadata field",
                            $"The multipart field '{fieldName}' is not supported.");
                }
            }

            if (storedObject is null || originalFileName is null || fileContentType is null)
            {
                throw InvalidRequest(
                    "Missing file",
                    $"Supply one video in the '{FileFieldName}' form field.");
            }

            var metadata = metadataValidator.Validate(title, description, hashtags);
            var video = new VideoRecord
            {
                Id = videoId,
                Title = metadata.Title,
                Description = metadata.Description,
                Hashtags = [.. metadata.Hashtags],
                OwnerId = null,
                OriginalFileName = originalFileName,
                ContentType = fileContentType,
                SizeBytes = sizeBytes,
                StorageBucket = storedObject.Bucket,
                StorageObjectKey = storedObject.ObjectKey,
                StorageEtag = storedObject.Etag,
                UploadedAtUtc = uploadedAtUtc,
                CorrelationId = correlationId,
                Status = VideoStatuses.Queued
            };
            var videoUploaded = new VideoUploadedV1(
                Guid.NewGuid(),
                VideoUploadedV1.Type,
                VideoUploadedV1.Version,
                uploadedAtUtc,
                videoId,
                storedObject.Bucket,
                storedObject.ObjectKey,
                storedObject.Etag,
                originalFileName,
                fileContentType,
                sizeBytes,
                metadata.Title,
                metadata.Description,
                metadata.Hashtags,
                OwnerId: null,
                uploadedAtUtc,
                correlationId);

            await submissionStore.SaveAsync(video, videoUploaded, topicName, cancellationToken);
            submissionCommitted = true;

            logger.LogInformation(
                "Stored video {VideoId} as {Bucket}/{ObjectKey} with {SizeBytes} bytes",
                videoId,
                storedObject.Bucket,
                storedObject.ObjectKey,
                sizeBytes);

            return new UploadResponse(
                videoId,
                metadata.Title,
                metadata.Description,
                metadata.Hashtags,
                VideoStatuses.Queued,
                originalFileName,
                fileContentType,
                sizeBytes,
                uploadedAtUtc,
                correlationId);
        }
        finally
        {
            if (objectStored && !submissionCommitted && objectKey is not null)
            {
                try
                {
                    await objectStorage.DeleteAsync(objectKey, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Compensation failed for orphaned object {Bucket}/{ObjectKey}; correlation {CorrelationId}",
                        objectStorage.BucketName,
                        objectKey,
                        correlationId);
                }
            }
        }
    }

    private static string GetBoundary(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(
                mediaType.MediaType.Value,
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadRequestException(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported request content type",
                "Use multipart/form-data to upload a video.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > MaximumBoundaryLength)
        {
            throw InvalidRequest(
                "Invalid multipart boundary",
                "The multipart boundary is missing or invalid.");
        }

        return boundary;
    }

    private static async Task<string> ReadTextFieldAsync(
        Stream stream,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1_024,
            leaveOpen: true);
        var value = new StringBuilder(Math.Min(maximumCharacters, 1_024));
        var buffer = new char[Math.Min(maximumCharacters + 1, 1_024)];

        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (value.Length + read > maximumCharacters)
            {
                throw InvalidRequest(
                    "Metadata field is too long",
                    $"A metadata field exceeded its {maximumCharacters}-character limit.");
            }

            value.Append(buffer, 0, read);
        }

        return value.ToString();
    }

    private static UploadRequestException InvalidRequest(string title, string detail) =>
        new(StatusCodes.Status400BadRequest, title, detail);
}
