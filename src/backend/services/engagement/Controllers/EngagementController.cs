using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.Api.Controllers;

[ApiController]
[Route("api/engagement")]
public sealed class EngagementController(
    RedisEngagementProjection cache,
    KnownVideoService knownVideos,
    EngagementKafkaPublisher publisher,
    CommentService comments,
    IOptions<EngagementOptions> options,
    TimeProvider timeProvider,
    ILogger<EngagementController> logger) : ControllerBase
{
    [HttpGet("videos/summaries")]
    public async Task<ActionResult<IReadOnlyList<VideoSummaryResponse>>> GetSummaries(
        [FromQuery] Guid[] ids,
        CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length > 50)
            throw new EngagementRequestException(400, "Too many videos", "At most 50 video summaries can be requested.");
        var responses = new List<VideoSummaryResponse>(distinct.Length);
        foreach (var id in distinct)
        {
            try { responses.Add(await cache.GetSummaryAsync(id, cancellationToken)); }
            catch (RedisException exception)
            {
                logger.LogWarning(exception, "Summary cache unavailable for {VideoId}", id);
                responses.Add(await GetDatabaseSummaryAsync(id, cancellationToken));
            }
        }
        return responses;
    }

    [HttpGet("videos/{videoId:guid}/reaction")]
    public async Task<ReactionResponse> GetReaction(Guid videoId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        await knownVideos.EnsureAvailableAsync(videoId, cancellationToken);
        try { return new(await cache.GetReactionAsync(videoId, userId, cancellationToken)); }
        catch (RedisException)
        {
            await using var dbContext = await HttpContext.RequestServices
                .GetRequiredService<IDbContextFactory<Data.EngagementDbContext>>()
                .CreateDbContextAsync(cancellationToken);
            return new(await dbContext.Reactions.Where(x => x.VideoId == videoId && x.UserId == userId)
                .Select(x => x.Value).SingleOrDefaultAsync(cancellationToken) ?? "none");
        }
    }

    [HttpPut("videos/{videoId:guid}/reaction")]
    public async Task<ActionResult<ReactionUpdateResponse>> PutReaction(
        Guid videoId,
        ReactionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = UserId();
        await knownVideos.EnsureAvailableAsync(videoId, cancellationToken);
        try { _ = await cache.GetSummaryAsync(videoId, cancellationToken); }
        catch (RedisException exception) { logger.LogWarning(exception, "Reaction cache hydration failed for {VideoId}", videoId); }

        var message = new VideoReactionChangedV1(
            Guid.NewGuid(), VideoReactionChangedV1.Type, 1, timeProvider.GetUtcNow(), videoId,
            userId, request.Reaction, HttpContext.TraceIdentifier);
        var delivered = await publisher.PublishReactionAsync(message, cancellationToken);
        try
        {
            var counts = await cache.ApplyReactionAsync(
                videoId, userId, request.Reaction, delivered.Offset.Value, cancellationToken);
            return StatusCode(202, new ReactionUpdateResponse(request.Reaction, counts.Likes, counts.Dislikes, false));
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Accepted reaction cache update is pending for {VideoId}", videoId);
            return StatusCode(202, new ReactionUpdateResponse(request.Reaction, null, null, true));
        }
    }

    [HttpPost("videos/{videoId:guid}/views")]
    public async Task<ActionResult<ViewAcceptedResponse>> RecordView(
        Guid videoId,
        ViewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ViewSessionId == Guid.Empty)
            throw new EngagementRequestException(400, "Invalid view session", "A non-empty viewSessionId is required.");
        await knownVideos.EnsureAvailableAsync(videoId, cancellationToken);
        try { _ = await cache.GetSummaryAsync(videoId, cancellationToken); }
        catch (RedisException exception) { logger.LogWarning(exception, "View cache hydration failed for {VideoId}", videoId); }

        var message = new VideoViewQualifiedV1(
            request.ViewSessionId, VideoViewQualifiedV1.Type, 1, timeProvider.GetUtcNow(), videoId,
            HttpContext.TraceIdentifier);
        await publisher.PublishViewAsync(message, cancellationToken);
        try
        {
            var result = await cache.IncrementViewOnceAsync(
                videoId, request.ViewSessionId, options.Value.ViewSessionTtlHours, cancellationToken);
            return StatusCode(202, new ViewAcceptedResponse(result.Counted, result.Count, false));
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Accepted view cache update is pending for {VideoId}", videoId);
            return StatusCode(202, new ViewAcceptedResponse(true, null, true));
        }
    }

    [HttpGet("videos/{videoId:guid}/comments")]
    public Task<CommentPageResponse> GetComments(
        Guid videoId,
        [FromQuery] int limit = 20,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        comments.GetPageAsync(videoId, limit, cursor, cancellationToken);

    [HttpPost("videos/{videoId:guid}/comments")]
    public async Task<ActionResult<CommentMutationResponse>> CreateComment(
        Guid videoId,
        CommentRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(201, await comments.CreateAsync(videoId, UserId(), request.Body, cancellationToken));

    [HttpPatch("comments/{commentId:guid}")]
    public Task<CommentMutationResponse> UpdateComment(
        Guid commentId,
        CommentRequest request,
        CancellationToken cancellationToken) =>
        comments.UpdateAsync(commentId, UserId(), request.Body, cancellationToken);

    [HttpDelete("comments/{commentId:guid}")]
    public Task<CommentDeletedResponse> DeleteComment(Guid commentId, CancellationToken cancellationToken) =>
        comments.DeleteAsync(commentId, UserId(), cancellationToken);

    private Guid UserId()
    {
        if (Guid.TryParse(Request.Headers["X-StreamForge-User-Id"].FirstOrDefault(), out var userId) && userId != Guid.Empty)
            return userId;
        throw new EngagementRequestException(401, "Authentication required", "Please log in.");
    }

    private async Task<VideoSummaryResponse> GetDatabaseSummaryAsync(Guid videoId, CancellationToken cancellationToken)
    {
        await using var dbContext = await HttpContext.RequestServices
            .GetRequiredService<IDbContextFactory<Data.EngagementDbContext>>()
            .CreateDbContextAsync(cancellationToken);
        var likes = await dbContext.Reactions.LongCountAsync(x => x.VideoId == videoId && x.Value == "like", cancellationToken);
        var dislikes = await dbContext.Reactions.LongCountAsync(x => x.VideoId == videoId && x.Value == "dislike", cancellationToken);
        var views = await dbContext.VideoViews.Where(x => x.VideoId == videoId).Select(x => (long?)x.Count)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var commentCount = await dbContext.Comments.LongCountAsync(x => x.VideoId == videoId, cancellationToken);
        return new(videoId, likes, dislikes, views, commentCount);
    }
}
