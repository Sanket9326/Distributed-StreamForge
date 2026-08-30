using StreamForge.Transcoding.Worker.Data.Entities;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Publishes one service-owned outbox message to Kafka.</summary>
public interface IKafkaOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
