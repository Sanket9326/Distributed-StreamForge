using System.Globalization;
using System.Text.Json;
using StreamForge.Engagement.Api.Data.Entities;

namespace StreamForge.Engagement.Api.Services;

public sealed record SubscriptionCacheState(Guid SubscriberId, Guid CreatorId, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int SourcePartition, string SourceOffset,
    string SortTime, bool Confirmed)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static SubscriptionCacheState From(UserSubscription row, bool confirmed = true) => new(
        row.SubscriberId, row.CreatorId, row.IsActive, row.CreatedAtUtc, row.UpdatedAtUtc, row.SourcePartition,
        row.SourceOffset.ToString(CultureInfo.InvariantCulture), SubscriptionCursorCodec.SortTime(row.CreatedAtUtc), confirmed);
}
