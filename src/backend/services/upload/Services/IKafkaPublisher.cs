using StreamForge.Upload.Api.Data.Entities;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Defines acknowledged publication of a durable outbox message to Kafka.
/// </summary>
public interface IKafkaPublisher
{
    /// <summary>Publishes one outbox message using its topic, partition key, payload, and event headers.</summary>
    /// <param name="message">The pending durable outbox message.</param>
    /// <param name="cancellationToken">Signals that publication should stop.</param>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
