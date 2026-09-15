using StreamForge.Engagement.Api.Models;
namespace StreamForge.Engagement.Api.Services;

public sealed record SubscriptionDelivery(int Partition, long Offset);
public interface ISubscriptionPublisher
{
    Task<SubscriptionDelivery> PublishSubscriptionAsync(UserSubscriptionChangedV1 message, CancellationToken ct);
    Task PublishSubscriptionDeadLetterAsync(SubscriptionDeadLetterV1 message, string key, CancellationToken ct);
}
