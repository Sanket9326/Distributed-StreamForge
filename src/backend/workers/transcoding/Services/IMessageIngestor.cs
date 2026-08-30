namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Durably converts one consumed Kafka envelope into a job or dead letter.</summary>
public interface IMessageIngestor
{
    Task IngestAsync(ConsumedEnvelope envelope, CancellationToken cancellationToken);
}

public sealed record ConsumedEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload);
