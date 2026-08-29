using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamForge.Upload.Api.Controllers;
using StreamForge.Upload.Api.Models;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class UploadsControllerTests
{
    [Fact]
    public async Task Upload_ForwardsHttpRequestToIngestionServiceAndReturnsCreated()
    {
        var receipt = new UploadResponse(
            Guid.NewGuid(),
            "Title",
            null,
            [],
            "queued",
            "source.mp4",
            "video/mp4",
            4,
            DateTimeOffset.Parse("2026-08-29T10:30:00Z"),
            "correlation-123");
        var ingestionService = new RecordingIngestionService(receipt);
        var requestBody = new MemoryStream([1, 2, 3, 4]);
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "correlation-123"
        };
        httpContext.Request.Body = requestBody;
        httpContext.Request.ContentType = "multipart/form-data; boundary=test";
        var controller = new UploadsController(ingestionService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.Upload(CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(receipt, created.Value);
        Assert.Same(requestBody, ingestionService.RequestBody);
        Assert.Equal(httpContext.Request.ContentType, ingestionService.RequestContentType);
        Assert.Equal(httpContext.TraceIdentifier, ingestionService.CorrelationId);
    }

    private sealed class RecordingIngestionService(UploadResponse response) : IVideoIngestionService
    {
        public Stream? RequestBody { get; private set; }

        public string? RequestContentType { get; private set; }

        public string? CorrelationId { get; private set; }

        public Task<UploadResponse> IngestAsync(
            Stream requestBody,
            string? requestContentType,
            string correlationId,
            CancellationToken cancellationToken)
        {
            RequestBody = requestBody;
            RequestContentType = requestContentType;
            CorrelationId = correlationId;
            return Task.FromResult(response);
        }
    }
}
