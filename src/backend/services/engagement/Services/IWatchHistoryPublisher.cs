using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.Api.Services;

public sealed record WatchHistoryDelivery(int Partition, long Offset);

public interface IWatchHistoryPublisher
{
    Task<WatchHistoryDelivery> PublishWatchHistoryAsync(UserWatchProgressSavedV1 message, CancellationToken ct);
    Task RepublishWatchHistoryAsync(WatchHistoryRetryEnvelope envelope, CancellationToken ct);
    Task PublishWatchHistoryDeadLetterAsync(WatchHistoryDeadLetterV1 message, string key, CancellationToken ct);
}
