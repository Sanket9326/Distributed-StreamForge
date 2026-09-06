using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class CommentService(
    IDbContextFactory<EngagementDbContext> contextFactory,
    KnownVideoService knownVideos,
    RedisEngagementProjection cache,
    CommentCursorCodec cursorCodec,
    TimeProvider timeProvider,
    ILogger<CommentService> logger)
{
    public async Task<CommentPageResponse> GetPageAsync(
        Guid videoId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 50)
            throw new EngagementRequestException(400, "Invalid page size", "The comment limit must be between 1 and 50.");
        await knownVideos.EnsureAvailableAsync(videoId, cancellationToken);
        var decoded = cursor is null ? ((DateTimeOffset CreatedAtUtc, Guid Id)?)null : cursorCodec.Decode(cursor);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Comments.AsNoTracking().Where(x => x.VideoId == videoId);
        if (decoded is { } value)
            query = query.Where(x => x.CreatedAtUtc < value.CreatedAtUtc ||
                x.CreatedAtUtc == value.CreatedAtUtc && x.Id.CompareTo(value.Id) < 0);
        var comments = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = comments.Count > limit;
        if (hasMore) comments.RemoveAt(comments.Count - 1);
        var total = await dbContext.Comments.LongCountAsync(x => x.VideoId == videoId, cancellationToken);
        var next = hasMore && comments.Count > 0
            ? cursorCodec.Encode(comments[^1].CreatedAtUtc, comments[^1].Id)
            : null;
        return new CommentPageResponse(comments.Select(Map).ToArray(), total, next);
    }

    public async Task<CommentMutationResponse> CreateAsync(
        Guid videoId,
        Guid userId,
        string body,
        CancellationToken cancellationToken)
    {
        var normalized = ValidateBody(body);
        await knownVideos.EnsureAvailableAsync(videoId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var comment = new Comment
        {
            Id = Guid.NewGuid(), VideoId = videoId, UserId = userId, Body = normalized,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);
        var count = await dbContext.Comments.LongCountAsync(x => x.VideoId == videoId, cancellationToken);
        await TrySetCountAsync(videoId, count);
        return new CommentMutationResponse(Map(comment), count);
    }

    public async Task<CommentMutationResponse> UpdateAsync(
        Guid commentId,
        Guid userId,
        string body,
        CancellationToken cancellationToken)
    {
        var normalized = ValidateBody(body);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var comment = await dbContext.Comments.SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken)
            ?? throw new EngagementRequestException(404, "Comment not found", "The requested comment does not exist.");
        EnsureOwner(comment, userId);
        comment.Body = normalized;
        comment.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        var count = await dbContext.Comments.LongCountAsync(x => x.VideoId == comment.VideoId, cancellationToken);
        return new CommentMutationResponse(Map(comment), count);
    }

    public async Task<CommentDeletedResponse> DeleteAsync(
        Guid commentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var comment = await dbContext.Comments.SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken)
            ?? throw new EngagementRequestException(404, "Comment not found", "The requested comment does not exist.");
        EnsureOwner(comment, userId);
        dbContext.Comments.Remove(comment);
        await dbContext.SaveChangesAsync(cancellationToken);
        var count = await dbContext.Comments.LongCountAsync(x => x.VideoId == comment.VideoId, cancellationToken);
        await TrySetCountAsync(comment.VideoId, count);
        return new CommentDeletedResponse(count);
    }

    private async Task TrySetCountAsync(Guid videoId, long count)
    {
        try { await cache.SetCommentCountAsync(videoId, count); }
        catch (RedisException exception) { logger.LogWarning(exception, "Comment count cache update failed for {VideoId}", videoId); }
    }

    private static string ValidateBody(string body)
    {
        var normalized = body.Trim();
        if (normalized.Length is < 1 or > 2_000)
            throw new EngagementRequestException(400, "Invalid comment", "Comments must contain between 1 and 2,000 characters.");
        return normalized;
    }

    private static void EnsureOwner(Comment comment, Guid userId)
    {
        if (comment.UserId != userId)
            throw new EngagementRequestException(403, "Comment access denied", "Only the comment author can change it.");
    }

    private static CommentResponse Map(Comment comment) => new(
        comment.Id, comment.VideoId, comment.UserId, comment.Body, comment.CreatedAtUtc, comment.UpdatedAtUtc);
}
