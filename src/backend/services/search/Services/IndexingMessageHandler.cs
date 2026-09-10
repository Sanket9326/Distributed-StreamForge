using StreamForge.Search.Api.Models;

namespace StreamForge.Search.Api.Services;

public sealed class IndexingMessageHandler(
    IVideoSearchIndex searchIndex,
    ISearchDeadLetterPublisher deadLetters,
    SearchTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<IndexingMessageHandler> logger)
{
    public async Task<IndexingHandleResult> HandleAsync(
        SearchConsumedEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var parsed = SearchEventParser.Parse(envelope.Payload);
        if (parsed.Event is null)
        {
            return await DeadLetterAsync(envelope, parsed.RejectionReason!, cancellationToken);
        }

        var result = await searchIndex.IndexAsync(parsed.Event, cancellationToken);
        switch (result)
        {
            case IndexWriteResult.Indexed:
                logger.LogInformation(
                    "Applied search indexing event {EventId} for video {VideoId} revision {Revision}",
                    parsed.Event.EventId,
                    parsed.Event.VideoId,
                    parsed.Event.Revision);
                return IndexingHandleResult.Completed;
            case IndexWriteResult.Superseded:
                logger.LogInformation(
                    "Completed superseded search indexing event {EventId} for video {VideoId} revision {Revision}",
                    parsed.Event.EventId,
                    parsed.Event.VideoId,
                    parsed.Event.Revision);
                return IndexingHandleResult.Completed;
            case IndexWriteResult.PermanentFailure:
                return await DeadLetterAsync(envelope, "permanent_mapping_error", cancellationToken);
            default:
                telemetry.RecordFailure("transient_elasticsearch_error");
                return IndexingHandleResult.Retry;
        }
    }

    private async Task<IndexingHandleResult> DeadLetterAsync(
        SearchConsumedEnvelope envelope,
        string reason,
        CancellationToken cancellationToken)
    {
        var deadLetter = new SearchIndexDeadLetterV1(
            Guid.NewGuid(),
            SearchIndexDeadLetterV1.Type,
            SearchIndexDeadLetterV1.Version,
            timeProvider.GetUtcNow(),
            envelope.Topic,
            envelope.Partition,
            envelope.Offset,
            envelope.Key,
            reason,
            envelope.Payload);
        try
        {
            await deadLetters.PublishAsync(deadLetter, envelope.Key ?? deadLetter.EventId.ToString("D"), cancellationToken);
            telemetry.RecordFailure(reason);
            logger.LogError(
                "Published search indexing failure at {Topic}:{Partition}:{Offset} to the dead-letter topic with reason {Reason}",
                envelope.Topic,
                envelope.Partition,
                envelope.Offset,
                reason);
            return IndexingHandleResult.Completed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            telemetry.RecordFailure("dead_letter_publish_error");
            logger.LogWarning(exception,
                "Dead-letter publication failed for {Topic}:{Partition}:{Offset}",
                envelope.Topic,
                envelope.Partition,
                envelope.Offset);
            return IndexingHandleResult.Retry;
        }
    }
}
