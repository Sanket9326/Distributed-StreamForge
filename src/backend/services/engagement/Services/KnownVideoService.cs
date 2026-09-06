using System.Net;
using Microsoft.EntityFrameworkCore;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;

namespace StreamForge.Engagement.Api.Services;

public sealed class KnownVideoService(
    IDbContextFactory<EngagementDbContext> contextFactory,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider)
{
    public async Task EnsureAvailableAsync(Guid videoId, CancellationToken cancellationToken)
    {
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (await dbContext.Videos.AsNoTracking().AnyAsync(x => x.VideoId == videoId, cancellationToken)) return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Head, $"api/feed/videos/{videoId:D}");
        using var response = await httpClientFactory.CreateClient("feed").SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new EngagementRequestException(404, "Video unavailable", "The requested video is not available.");
        if (!response.IsSuccessStatusCode)
            throw new EngagementRequestException(503, "Video validation unavailable", "The video could not be validated. Retry later.");

        await RecordAsync(videoId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task RecordAsync(Guid videoId, DateTimeOffset availableAtUtc, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO engagement.videos (video_id, available_at_utc)
            VALUES ({{videoId}}, {{availableAtUtc}})
            ON CONFLICT (video_id) DO UPDATE
            SET available_at_utc = LEAST(engagement.videos.available_at_utc, EXCLUDED.available_at_utc)
            """, cancellationToken);
    }
}
