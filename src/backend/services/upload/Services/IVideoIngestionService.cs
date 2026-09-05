using StreamForge.Upload.Api.Models;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Defines the application workflow that turns a multipart request into a durable queued video.
/// </summary>
public interface IVideoIngestionService
{
    /// <summary>
    /// Streams, validates, stores, and records one video submission.
    /// </summary>
    /// <param name="requestBody">The unbuffered multipart request stream.</param>
    /// <param name="requestContentType">The multipart content type, including its boundary.</param>
    /// <param name="correlationId">The validated request correlation ID.</param>
    /// <param name="ownerId">The verified account that owns the new video.</param>
    /// <param name="cancellationToken">Signals that request processing should stop.</param>
    /// <returns>The receipt for the durably stored, queued video.</returns>
    Task<UploadResponse> IngestAsync(
        Stream requestBody,
        string? requestContentType,
        string correlationId,
        Guid ownerId,
        CancellationToken cancellationToken);
}
