using Microsoft.AspNetCore.Mvc;
using StreamForge.Upload.Api.Infrastructure;
using StreamForge.Upload.Api.Models;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.Api.Controllers;

/// <summary>
/// Receives video upload HTTP requests and delegates ingestion to the application service.
/// </summary>
/// <param name="ingestionService">The application workflow that performs durable ingestion.</param>
[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(IVideoIngestionService ingestionService) : ControllerBase
{
    /// <summary>
    /// Accepts a streamed multipart upload and returns the durable ingestion receipt.
    /// </summary>
    /// <param name="cancellationToken">Signals that the client disconnected or canceled the request.</param>
    /// <returns>A 201 response containing the queued video receipt.</returns>
    [HttpPost]
    [DisableFormValueModelBinding]
    [ProducesResponseType<UploadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UploadResponse>> Upload(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(Request.Headers["X-StreamForge-User-Id"], out var ownerId) || ownerId == Guid.Empty)
            return Unauthorized(new ProblemDetails
            {
                Status = 401, Title = "An authenticated uploader is required.",
                Extensions = { ["code"] = "session_invalid" }
            });
        var response = await ingestionService.IngestAsync(
            Request.Body,
            Request.ContentType,
            HttpContext.TraceIdentifier,
            ownerId,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
