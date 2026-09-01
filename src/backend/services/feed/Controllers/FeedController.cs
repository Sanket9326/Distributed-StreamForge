using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StreamForge.Feed.Api.Models;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.Api.Controllers;

[ApiController]
[Route("api/feed/videos")]
public sealed class FeedController(
    FeedQueryService feedQuery,
    CompletionNotifier completionNotifier) : ControllerBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    [ProducesResponseType<FeedPageResponse>(StatusCodes.Status200OK)]
    public Task<FeedPageResponse> GetPage(
        [FromQuery] int limit = 10,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        feedQuery.GetPageAsync(limit, cursor, cancellationToken);

    [HttpGet("{videoId:guid}/renditions")]
    [ProducesResponseType<IReadOnlyList<FeedRenditionResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<FeedRenditionResponse>> GetRenditions(
        Guid videoId,
        CancellationToken cancellationToken) =>
        feedQuery.GetRenditionsAsync(videoId, cancellationToken);

    [HttpGet("{videoId:guid}/completion-events")]
    public async Task CompletionEvents(Guid videoId, CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Append("X-Accel-Buffering", "no");
        await Response.WriteAsync("retry: 5000\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        using var subscription = completionNotifier.Subscribe(videoId);
        var alreadyCompleted = await feedQuery.GetCompletionAsync(videoId, cancellationToken);
        if (alreadyCompleted is not null)
        {
            await WriteCompletedAsync(videoId, alreadyCompleted.Value, cancellationToken);
            return;
        }

        var readTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            var heartbeatTask = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            var completed = await Task.WhenAny(readTask, heartbeatTask);
            if (completed == readTask)
            {
                var notification = await readTask;
                await WriteCompletedAsync(
                    notification.VideoId,
                    notification.AvailableAtUtc,
                    cancellationToken);
                return;
            }

            await Response.WriteAsync(": heartbeat\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private async Task WriteCompletedAsync(
        Guid videoId,
        DateTimeOffset availableAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new CompletionEventResponse(videoId, availableAtUtc),
            SerializerOptions);
        await Response.WriteAsync($"event: completed\ndata: {payload}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
