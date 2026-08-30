using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StreamForge.Transcoding.Worker.Data;
using StreamForge.Transcoding.Worker.Data.Entities;
using StreamForge.Transcoding.Worker.Models;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Persists input offsets and creates deduplicated jobs before Kafka acknowledgement.</summary>
public sealed class MessageIngestor(
    IDbContextFactory<TranscodingDbContext> contextFactory,
    OutcomeMessageFactory outcomeFactory,
    TranscodingTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<MessageIngestor> logger) : IMessageIngestor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task IngestAsync(ConsumedEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            await IngestCoreAsync(dbContext, envelope, cancellationToken);
        });
    }

    private async Task IngestCoreAsync(
        TranscodingDbContext dbContext,
        ConsumedEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ConsumedMessages.AnyAsync(message =>
            message.Topic == envelope.Topic &&
            message.Partition == envelope.Partition &&
            message.Offset == envelope.Offset,
            cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var validation = DeserializeAndValidate(envelope.Payload);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        Guid? eventId = validation.Event?.EventId;

        if (validation.Event is not null)
        {
            var existing = await dbContext.Jobs.FindAsync([validation.Event.EventId], cancellationToken);
            if (existing is null)
            {
                dbContext.Jobs.Add(new TranscodingJob
                {
                    EventId = validation.Event.EventId,
                    VideoId = validation.Event.VideoId,
                    SourceBucket = validation.Event.Bucket,
                    SourceObjectKey = validation.Event.ObjectKey,
                    SourceEtag = validation.Event.Etag.Trim('"'),
                    SourceSizeBytes = validation.Event.SizeBytes,
                    OriginalFileName = validation.Event.OriginalFileName,
                    SourceContentType = validation.Event.ContentType,
                    CorrelationId = validation.Event.CorrelationId,
                    Status = JobStatuses.Queued,
                    NextAttemptAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                logger.LogInformation(
                    "Accepted transcoding event {EventId} for video {VideoId}",
                    validation.Event.EventId,
                    validation.Event.VideoId);
                telemetry.AcceptedJobs.Add(1);
            }
            else if (existing.VideoId != validation.Event.VideoId)
            {
                dbContext.OutboxMessages.Add(outcomeFactory.CreateDeadLetter(
                    envelope,
                    "event_id_conflict",
                    "The event ID was previously associated with another video.",
                    now));
                telemetry.DeadLetters.Add(1, new KeyValuePair<string, object?>("reason", "event_id_conflict"));
            }
        }
        else
        {
            dbContext.OutboxMessages.Add(outcomeFactory.CreateDeadLetter(
                envelope,
                validation.ErrorCode!,
                validation.ErrorReason!,
                now));
            telemetry.DeadLetters.Add(1, new KeyValuePair<string, object?>("reason", validation.ErrorCode));
        }

        dbContext.ConsumedMessages.Add(new ConsumedKafkaMessage
        {
            Topic = envelope.Topic,
            Partition = envelope.Partition,
            Offset = envelope.Offset,
            EventId = eventId,
            ConsumedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static ValidationResult DeserializeAndValidate(string payload)
    {
        VideoUploadedV1? videoUploaded;
        try
        {
            videoUploaded = JsonSerializer.Deserialize<VideoUploadedV1>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            return ValidationResult.Invalid("invalid_json", "The input payload is not valid VideoUploadedV1 JSON.");
        }

        if (videoUploaded is null)
        {
            return ValidationResult.Invalid("empty_event", "The input payload did not contain an event.");
        }

        if (videoUploaded.EventType != VideoUploadedV1.Type || videoUploaded.EventVersion != VideoUploadedV1.Version)
        {
            return ValidationResult.Invalid("unsupported_contract", "The input event type or version is unsupported.");
        }

        if (videoUploaded.EventId == Guid.Empty || videoUploaded.VideoId == Guid.Empty ||
            string.IsNullOrWhiteSpace(videoUploaded.Bucket) ||
            string.IsNullOrWhiteSpace(videoUploaded.ObjectKey) ||
            string.IsNullOrWhiteSpace(videoUploaded.Etag) ||
            videoUploaded.SizeBytes <= 0 ||
            string.IsNullOrWhiteSpace(videoUploaded.CorrelationId))
        {
            return ValidationResult.Invalid("invalid_contract", "The input event is missing required source coordinates or identifiers.");
        }

        return ValidationResult.Valid(videoUploaded);
    }

    private sealed record ValidationResult(VideoUploadedV1? Event, string? ErrorCode, string? ErrorReason)
    {
        public static ValidationResult Valid(VideoUploadedV1 videoUploaded) => new(videoUploaded, null, null);

        public static ValidationResult Invalid(string code, string reason) => new(null, code, reason);
    }
}
