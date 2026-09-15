using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class SubscriptionProjector(IDbContextFactory<EngagementDbContext> contexts, TimeProvider clock)
{
    public async Task<UserSubscription?> ProjectAsync(UserSubscriptionChangedV1 message,
        string topic, int partition, long offset, CancellationToken ct)
    {
        await using var strategyDb = await contexts.CreateDbContextAsync(ct);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Serialize rebalanced consumers for this pair before checking the receipt.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({message.Key}, 0))", ct);
            var duplicate = await db.ConsumedMessages.AnyAsync(x =>
                x.EventId == message.EventId || x.Topic == topic && x.Partition == partition && x.Offset == offset, ct);
            if (!duplicate)
            {
                var current = await db.Subscriptions.SingleOrDefaultAsync(
                    x => x.SubscriberId == message.SubscriberId && x.CreatorId == message.CreatorId, ct);
                if (current is not null && current.SourcePartition != partition)
                    throw new InvalidOperationException("Subscription partition changed; restore the configured topic partition count.");
                if (current is null)
                {
                    db.Subscriptions.Add(new UserSubscription
                    {
                        SubscriberId = message.SubscriberId, CreatorId = message.CreatorId, IsActive = message.IsActive,
                        CreatedAtUtc = message.OccurredAtUtc, UpdatedAtUtc = message.OccurredAtUtc,
                        SourcePartition = partition, SourceOffset = offset
                    });
                }
                else if (offset > current.SourceOffset)
                {
                    if (message.IsActive && !current.IsActive) current.CreatedAtUtc = message.OccurredAtUtc;
                    current.IsActive = message.IsActive;
                    current.UpdatedAtUtc = message.OccurredAtUtc;
                    current.SourceOffset = offset;
                }
                db.ConsumedMessages.Add(new ConsumedKafkaMessage
                {
                    Topic = topic, Partition = partition, Offset = offset, EventId = message.EventId, ConsumedAtUtc = clock.GetUtcNow()
                });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            // A replay still returns durable state so the caller can repair Redis before committing Kafka.
            return await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(
                x => x.SubscriberId == message.SubscriberId && x.CreatorId == message.CreatorId, ct);
        });
    }
}
