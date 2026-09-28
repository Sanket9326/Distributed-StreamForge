using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed record WatchHistoryCacheState(Guid UserId, Guid VideoId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long PositionMs,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long DurationMs,
    bool IsCompleted, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int SourcePartition,
    string SourceOffset, string SortTime, bool Confirmed)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static WatchHistoryCacheState From(WatchHistory row, bool confirmed = true) => new(
        row.UserId, row.VideoId, row.PositionMs, row.DurationMs, row.IsCompleted, row.CreatedAtUtc,
        row.UpdatedAtUtc, row.SourcePartition, row.SourceOffset.ToString(CultureInfo.InvariantCulture),
        SubscriptionCursorCodec.SortTime(row.UpdatedAtUtc), confirmed);
    public WatchHistoryItem Item() => new(VideoId, PositionMs, DurationMs, IsCompleted,
        CreatedAtUtc, UpdatedAtUtc, SourcePartition, SourceOffset);
}
