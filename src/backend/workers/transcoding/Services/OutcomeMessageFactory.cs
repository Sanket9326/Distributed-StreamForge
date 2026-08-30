using System.Text.Json;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Data.Entities;
using StreamForge.Transcoding.Worker.Models;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Builds versioned outcome and dead-letter outbox messages.</summary>
public sealed class OutcomeMessageFactory(IOptions<KafkaOptions> options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly KafkaOptions kafkaOptions = options.Value;

    public OutboxMessage CreateCompleted(
        TranscodingJob job,
        IReadOnlyList<ProcessedRendition> renditions,
        DateTimeOffset occurredAtUtc)
    {
        var contract = new VideoTranscodingCompletedV1(
            Guid.NewGuid(),
            VideoTranscodingCompletedV1.Type,
            VideoTranscodingCompletedV1.Version,
            occurredAtUtc,
            job.EventId,
            job.VideoId,
            job.SourceBucket,
            job.SourceObjectKey,
            job.SourceEtag,
            renditions.Select(rendition => new RenditionV1(
                rendition.Tier,
                rendition.Width,
                rendition.Height,
                rendition.VideoCodec,
                rendition.AudioCodec,
                rendition.ContentType,
                rendition.Bucket,
                rendition.ObjectKey,
                rendition.Etag,
                rendition.SizeBytes)).ToArray(),
            job.CorrelationId);
        return Create(
            contract.EventId,
            job.VideoId,
            contract.EventType,
            contract.EventVersion,
            kafkaOptions.CompletedTopic,
            job.VideoId.ToString("D"),
            contract,
            occurredAtUtc);
    }

    public OutboxMessage CreateFailed(
        TranscodingJob job,
        string failureCode,
        string failureReason,
        DateTimeOffset occurredAtUtc)
    {
        var contract = new VideoTranscodingFailedV1(
            Guid.NewGuid(),
            VideoTranscodingFailedV1.Type,
            VideoTranscodingFailedV1.Version,
            occurredAtUtc,
            job.EventId,
            job.VideoId,
            failureCode,
            failureReason,
            job.AttemptCount,
            job.CorrelationId);
        return Create(
            contract.EventId,
            job.VideoId,
            contract.EventType,
            contract.EventVersion,
            kafkaOptions.FailedTopic,
            job.VideoId.ToString("D"),
            contract,
            occurredAtUtc);
    }

    public OutboxMessage CreateDeadLetter(
        ConsumedEnvelope envelope,
        string rejectionCode,
        string rejectionReason,
        DateTimeOffset occurredAtUtc)
    {
        var eventId = Guid.NewGuid();
        var contract = new VideoProcessingDeadLetterV1(
            eventId,
            VideoProcessingDeadLetterV1.Type,
            VideoProcessingDeadLetterV1.Version,
            occurredAtUtc,
            envelope.Topic,
            envelope.Partition,
            envelope.Offset,
            envelope.Key,
            rejectionCode,
            rejectionReason,
            envelope.Payload);
        var partitionKey = envelope.Key ?? $"{envelope.Topic}:{envelope.Partition}:{envelope.Offset}";
        return Create(
            eventId,
            null,
            contract.EventType,
            contract.EventVersion,
            kafkaOptions.DeadLetterTopic,
            partitionKey,
            contract,
            occurredAtUtc);
    }

    private static OutboxMessage Create<T>(
        Guid eventId,
        Guid? videoId,
        string type,
        int version,
        string topic,
        string partitionKey,
        T payload,
        DateTimeOffset occurredAtUtc) =>
        new()
        {
            Id = eventId,
            VideoId = videoId,
            Type = type,
            Version = version,
            Topic = topic,
            PartitionKey = partitionKey,
            Payload = JsonSerializer.Serialize(payload, SerializerOptions),
            OccurredAtUtc = occurredAtUtc,
            NextAttemptAtUtc = occurredAtUtc
        };
}
