using StreamForge.Feed.Api.Data.Entities;

namespace StreamForge.Feed.Api.Services;

public interface IFeedOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
