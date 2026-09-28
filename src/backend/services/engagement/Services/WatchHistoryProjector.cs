using Microsoft.EntityFrameworkCore;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryProjector(IDbContextFactory<EngagementDbContext> contexts, TimeProvider clock)
{
    public async Task<WatchHistory> ProjectAsync(UserWatchProgressSavedV1 message,
        string topic, int originalPartition, long originalOffset, CancellationToken ct)
    {
        await using var strategyDb = await contexts.CreateDbContextAsync(ct);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"watch-history:" + message.Key}, 0))", ct);
            var current = await db.WatchHistory.SingleOrDefaultAsync(
                x => x.UserId == message.UserId && x.VideoId == message.VideoId, ct);
            if (current is not null && current.SourcePartition != originalPartition)
                throw new InvalidOperationException("Watch history partition changed; restore the fixed partition count.");
            var duplicate = await db.ConsumedMessages.AnyAsync(x => x.EventId == message.EventId ||
                x.Topic == topic && x.Partition == originalPartition && x.Offset == originalOffset, ct);
            if (!duplicate)
            {
                if (current is null)
                {
                    current = new WatchHistory { UserId = message.UserId, VideoId = message.VideoId,
                        CreatedAtUtc = message.OccurredAtUtc, SourcePartition = originalPartition, SourceOffset = -1 };
                    db.WatchHistory.Add(current);
                }
                if (originalOffset > current.SourceOffset)
                {
                    current.PositionMs = message.PositionMs;
                    current.DurationMs = message.DurationMs;
                    current.IsCompleted = message.IsCompleted;
                    current.UpdatedAtUtc = message.OccurredAtUtc;
                    current.SourceOffset = originalOffset;
                }
                db.ConsumedMessages.Add(new ConsumedKafkaMessage { Topic = topic, Partition = originalPartition,
                    Offset = originalOffset, EventId = message.EventId, ConsumedAtUtc = clock.GetUtcNow() });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return current ?? throw new InvalidOperationException("Watch history receipt has no projection.");
        });
    }
}
