using System.Globalization;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed class SubscriptionService(SubscriptionCache cache, ISubscriptionPublisher publisher,
    IDbContextFactory<EngagementDbContext> contexts, IHttpClientFactory clients, TimeProvider clock,
    ILogger<SubscriptionService> logger)
{
    public async Task<SubscriptionMutation> ChangeAsync(Guid actor, Guid counterpart, bool active, bool removeSubscriber,
        string correlationId, CancellationToken ct)
    {
        if (counterpart == Guid.Empty || counterpart == actor)
            throw new EngagementRequestException(400, "Invalid subscription", "Choose a different user.");
        var subscriber = removeSubscriber ? counterpart : actor;
        var creator = removeSubscriber ? actor : counterpart;
        if (active)
        {
            var profiles = await clients.CreateClient("identity").GetFromJsonAsync<Profile[]>(
                $"api/users?ids={creator:D}", ct) ?? [];
            if (!profiles.Any(x => x.Id == creator))
                throw new EngagementRequestException(404, "User not found", "This account is unavailable.");
        }
        var now = clock.GetUtcNow();
        now = new DateTimeOffset(now.UtcTicks / 10 * 10, TimeSpan.Zero); // PostgreSQL microsecond precision.
        var message = new UserSubscriptionChangedV1(Guid.NewGuid(), UserSubscriptionChangedV1.Type, 1,
            now, subscriber, creator, actor, active, correlationId);
        var delivery = await publisher.PublishSubscriptionAsync(message, ct);
        var state = SubscriptionCacheState.From(new UserSubscription
        {
            SubscriberId = subscriber, CreatorId = creator, IsActive = active, CreatedAtUtc = now, UpdatedAtUtc = now,
            SourcePartition = delivery.Partition, SourceOffset = delivery.Offset
        }, false);
        try
        {
            state = await cache.ApplyAsync(state, ct);
            return new(subscriber, creator, state.IsActive, false, state.SourcePartition, state.SourceOffset);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or OperationCanceledException)
        {
            logger.LogWarning("Subscription accepted with cache update pending for event {EventId}", message.EventId);
            return new(subscriber, creator, active, true, delivery.Partition, delivery.Offset.ToString(CultureInfo.InvariantCulture));
        }
    }

    public async Task<SubscriptionPage> GetPageAsync(Guid user, bool incoming, int limit, string? cursor, CancellationToken ct)
    {
        if (limit is < 1 or > 50)
            throw new EngagementRequestException(400, "Invalid page size", "The limit must be between 1 and 50.");
        try { return await cache.GetPageAsync(user, incoming, limit, cursor, ct); }
        catch (Exception exception) when (exception is RedisException or TimeoutException ||
            exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return await cache.GetDatabasePageAsync(user, incoming, limit, cursor, ct);
        }
    }

    public async Task<IReadOnlyList<SubscriptionStatus>> GetStatusAsync(Guid user, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length > 50 || ids.Any(x => x == Guid.Empty))
            throw new EngagementRequestException(400, "Invalid creators", "Request at most 50 non-empty creator IDs.");
        ids = ids.Distinct().ToArray();
        try { return await cache.GetStatusAsync(user, ids, ct); }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var rows = await db.Subscriptions.AsNoTracking().Where(x => x.SubscriberId == user && ids.Contains(x.CreatorId)).ToListAsync(ct);
            return ids.Select(id =>
            {
                var row = rows.SingleOrDefault(x => x.CreatorId == id);
                return new SubscriptionStatus(id, row?.IsActive ?? false, row?.SourcePartition,
                    row?.SourceOffset.ToString(CultureInfo.InvariantCulture));
            }).ToArray();
        }
    }
    private sealed record Profile(Guid Id, string Username);
}
