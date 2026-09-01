using Microsoft.EntityFrameworkCore;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Models;

namespace StreamForge.Feed.Api.Services;

public sealed class FeedQueryService(
    IDbContextFactory<FeedDbContext> contextFactory,
    FeedCursorCodec cursorCodec,
    IPlaybackUrlSigner playbackUrlSigner)
{
    public async Task<FeedPageResponse> GetPageAsync(
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 10)
        {
            throw new FeedRequestException(
                StatusCodes.Status400BadRequest,
                "Invalid page size",
                "The feed limit must be between 1 and 10.");
        }

        var cursorSortKey = cursor is null ? null : cursorCodec.Decode(cursor);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Videos
            .AsNoTracking()
            .Where(video =>
                video.HasMetadata &&
                video.HasCompletion &&
                video.SortKey != null &&
                video.Renditions.Any());
        if (cursorSortKey is not null)
        {
            query = query.Where(video => video.SortKey!.CompareTo(cursorSortKey) < 0);
        }

        var videos = await query
            .OrderByDescending(video => video.SortKey)
            .Include(video => video.Renditions)
            .AsSplitQuery()
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = videos.Count > limit;
        if (hasMore)
        {
            videos.RemoveAt(videos.Count - 1);
        }

        var items = new List<FeedVideoResponse>(videos.Count);
        foreach (var video in videos)
        {
            items.Add(await MapAsync(video, cancellationToken));
        }

        var nextCursor = hasMore && videos.Count > 0
            ? cursorCodec.Encode(videos[^1].SortKey!)
            : null;
        return new FeedPageResponse(items, nextCursor);
    }

    public async Task<IReadOnlyList<FeedRenditionResponse>> GetRenditionsAsync(
        Guid videoId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var video = await dbContext.Videos
            .AsNoTracking()
            .Include(candidate => candidate.Renditions)
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == videoId &&
                candidate.HasMetadata &&
                candidate.HasCompletion,
                cancellationToken);
        if (video is null || video.Renditions.Count == 0)
        {
            throw new FeedRequestException(
                StatusCodes.Status404NotFound,
                "Video unavailable",
                "The requested video is not available in the feed.");
        }

        return await MapRenditionsAsync(video.Renditions, cancellationToken);
    }

    public async Task<DateTimeOffset?> GetCompletionAsync(
        Guid videoId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Videos
            .AsNoTracking()
            .Where(video => video.Id == videoId && video.HasCompletion)
            .Select(video => video.AvailableAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<FeedVideoResponse> MapAsync(
        FeedVideo video,
        CancellationToken cancellationToken) => new(
            video.Id,
            video.Title!,
            video.Description,
            video.Hashtags,
            video.UploadedAtUtc!.Value,
            video.AvailableAtUtc!.Value,
            await MapRenditionsAsync(video.Renditions, cancellationToken));

    private async Task<IReadOnlyList<FeedRenditionResponse>> MapRenditionsAsync(
        IReadOnlyCollection<FeedRendition> renditions,
        CancellationToken cancellationToken)
    {
        var ordered = renditions
            .OrderByDescending(rendition => rendition.Height)
            .ThenByDescending(rendition => rendition.Width)
            .ThenBy(rendition => rendition.Tier, StringComparer.Ordinal)
            .ToArray();
        var responses = new FeedRenditionResponse[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            var rendition = ordered[index];
            var signed = await playbackUrlSigner.SignAsync(rendition, cancellationToken);
            responses[index] = new FeedRenditionResponse(
                rendition.Tier,
                rendition.Width,
                rendition.Height,
                rendition.VideoCodec,
                rendition.AudioCodec,
                rendition.ContentType,
                rendition.SizeBytes,
                signed.Url,
                signed.ExpiresAtUtc);
        }

        return responses;
    }
}
