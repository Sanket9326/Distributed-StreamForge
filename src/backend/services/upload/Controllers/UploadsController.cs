using System.Buffers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using StreamForge.Upload.Api.Models;
using StreamForge.Upload.Api.Options;
using StreamForge.Upload.Api.Services;
using StreamForge.Upload.Api.Infrastructure;

namespace StreamForge.Upload.Api.Controllers;

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(
    VideoFileValidator validator,
    UploadStorage storage,
    IOptions<UploadStorageOptions> options,
    TimeProvider timeProvider,
    ILogger<UploadsController> logger) : ControllerBase
{
    private const string FileFieldName = "file";
    private const int MaximumBoundaryLength = 128;
    private readonly long maxFileSizeBytes = options.Value.MaxFileSizeBytes;

    [HttpPost]
    [DisableFormValueModelBinding]
    [ProducesResponseType<UploadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UploadResponse>> Upload(CancellationToken cancellationToken)
    {
        UploadTarget? target = null;
        var committed = false;

        try
        {
            var boundary = GetBoundary(Request.ContentType);
            var reader = new MultipartReader(boundary, Request.Body);
            string? originalFileName = null;
            string? contentType = null;
            long sizeBytes = 0;

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var submittedFileName = HeaderUtilities.RemoveQuotes(
                    disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                if (string.IsNullOrWhiteSpace(submittedFileName))
                {
                    continue;
                }

                var fieldName = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (!string.Equals(fieldName, FileFieldName, StringComparison.Ordinal))
                {
                    throw new UploadRequestException(
                        StatusCodes.Status400BadRequest,
                        "Invalid file field",
                        $"The video must be supplied in the '{FileFieldName}' form field.");
                }

                if (target is not null)
                {
                    throw new UploadRequestException(
                        StatusCodes.Status400BadRequest,
                        "Too many files",
                        "Only one video can be uploaded per request.");
                }

                originalFileName = Path.GetFileName(submittedFileName);
                contentType = section.ContentType;
                var extension = validator.Validate(originalFileName, contentType);
                target = storage.CreateTarget(extension);

                await using var output = storage.OpenTemporaryFile(target);
                sizeBytes = await CopyWithLimitAsync(
                    section.Body,
                    output,
                    maxFileSizeBytes,
                    cancellationToken);

                logger.LogDebug("Read {SizeBytes} upload bytes from multipart section", sizeBytes);

                if (sizeBytes == 0)
                {
                    throw new UploadRequestException(
                        StatusCodes.Status400BadRequest,
                        "Empty file",
                        "The uploaded video must not be empty.");
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (target is null || originalFileName is null || contentType is null)
            {
                throw new UploadRequestException(
                    StatusCodes.Status400BadRequest,
                    "Missing file",
                    $"Supply one video in the '{FileFieldName}' form field.");
            }

            storage.Commit(target);
            committed = true;

            var response = new UploadResponse(
                target.Id,
                originalFileName,
                contentType,
                sizeBytes,
                timeProvider.GetUtcNow(),
                HttpContext.TraceIdentifier);

            logger.LogInformation(
                "Stored upload {UploadId} ({FileName}, {SizeBytes} bytes)",
                target.Id,
                originalFileName,
                sizeBytes);

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (UploadRequestException exception)
        {
            return CreateProblem(exception.StatusCode, exception.Title, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Rejected malformed multipart upload");
            return CreateProblem(
                StatusCodes.Status400BadRequest,
                "Malformed multipart request",
                "The multipart request could not be read.");
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Failed to store uploaded video");
            return CreateProblem(
                StatusCodes.Status500InternalServerError,
                "Upload storage failure",
                "The video could not be stored. Retry the upload later.");
        }
        finally
        {
            if (!committed && target is not null)
            {
                try
                {
                    storage.DeleteTemporaryFile(target);
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Failed to remove partial upload {UploadId}", target.Id);
                }
            }
        }
    }

    private static string GetBoundary(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadRequestException(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported request content type",
                "Use multipart/form-data to upload a video.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > MaximumBoundaryLength)
        {
            throw new UploadRequestException(
                StatusCodes.Status400BadRequest,
                "Invalid multipart boundary",
                "The multipart boundary is missing or invalid.");
        }

        return boundary;
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        long totalBytes = 0;

        try
        {
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maximumBytes)
                {
                    throw new UploadRequestException(
                        StatusCodes.Status413PayloadTooLarge,
                        "Video is too large",
                        $"The video cannot exceed {maximumBytes} bytes.");
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            return totalBytes;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private ObjectResult CreateProblem(int statusCode, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
