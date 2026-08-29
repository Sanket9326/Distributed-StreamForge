using StreamForge.Upload.Api.Data;
using StreamForge.Upload.Api.Data.Entities;
using StreamForge.Upload.Api.Models;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Persists the video record and matching outbox message in one EF Core save transaction.
/// </summary>
public sealed class VideoSubmissionStore(
    UploadDbContext dbContext,
    OutboxMessageFactory outboxMessageFactory)
{
    /// <summary>Adds a video and its publication intent to PostgreSQL atomically.</summary>
    /// <param name="video">The authoritative ingestion metadata row.</param>
    /// <param name="videoUploaded">The event that must eventually reach Kafka.</param>
    /// <param name="topic">The Kafka destination topic.</param>
    /// <param name="cancellationToken">Signals that persistence should stop.</param>
    public async Task SaveAsync(
        VideoRecord video,
        VideoUploadedV1 videoUploaded,
        string topic,
        CancellationToken cancellationToken)
    {
        var outboxMessage = outboxMessageFactory.Create(videoUploaded, topic);

        dbContext.Videos.Add(video);
        dbContext.OutboxMessages.Add(outboxMessage);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
