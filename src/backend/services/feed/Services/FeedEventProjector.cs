using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Models;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class FeedEventProjector(
    IDbContextFactory<FeedDbContext> contextFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<ObjectStorageOptions> storageOptions,
    TimeProvider timeProvider,
    ILogger<FeedEventProjector> logger) : IFeedEventProjector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly KafkaOptions topics = kafkaOptions.Value;
    private readonly ObjectStorageOptions storage = storageOptions.Value;

    public async Task<ProjectionResult> ProjectAsync(
        ConsumedEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await ProjectCoreAsync(dbContext, envelope, cancellationToken);
        });
    }

    private async Task<ProjectionResult> ProjectCoreAsync(
        FeedDbContext dbContext,
        ConsumedEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ConsumedMessages.AnyAsync(message =>
            message.Topic == envelope.Topic &&
            message.Partition == envelope.Partition &&
            message.Offset == envelope.Offset,
            cancellationToken))
        {
            return ProjectionResult.None;
        }

        var parsed = Parse(envelope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (parsed.EventId is not null && await dbContext.ConsumedMessages.AnyAsync(
            message => message.EventId == parsed.EventId,
            cancellationToken))
        {
            dbContext.ConsumedMessages.Add(CreateConsumed(envelope, null, "duplicate_event"));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProjectionResult.None;
        }

        ProjectionResult result = ProjectionResult.None;
        if (parsed.Uploaded is not null)
        {
            await ApplyUploadedAsync(dbContext, parsed.Uploaded, cancellationToken);
        }
        else if (parsed.Completed is not null)
        {
            await ApplyCompletedAsync(dbContext, parsed.Completed, cancellationToken);
            result = ProjectionResult.Completed(parsed.Completed.VideoId, parsed.Completed.OccurredAtUtc);
        }
        else
        {
            logger.LogWarning(
                "Rejected feed event at {Topic}:{Partition}:{Offset} with code {RejectionCode}",
                envelope.Topic,
                envelope.Partition,
                envelope.Offset,
                parsed.RejectionCode);
        }

        dbContext.ConsumedMessages.Add(CreateConsumed(
            envelope,
            parsed.EventId,
            parsed.RejectionCode));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task ApplyUploadedAsync(
        FeedDbContext dbContext,
        VideoUploadedV1 uploaded,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var video = await dbContext.Videos.FindAsync([uploaded.VideoId], cancellationToken);
        if (video is null)
        {
            video = new FeedVideo
            {
                Id = uploaded.VideoId,
                CreatedAtUtc = now
            };
            dbContext.Videos.Add(video);
        }

        video.Title = uploaded.Title.Trim();
        video.Description = string.IsNullOrWhiteSpace(uploaded.Description)
            ? null
            : uploaded.Description.Trim();
        video.Hashtags = uploaded.Hashtags.ToArray();
        video.OwnerId = uploaded.OwnerId;
        video.UploadedAtUtc = uploaded.UploadedAtUtc;
        video.HasMetadata = true;
        video.UpdatedAtUtc = now;
    }

    private async Task ApplyCompletedAsync(
        FeedDbContext dbContext,
        VideoTranscodingCompletedV1 completed,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var video = await dbContext.Videos
            .Include(candidate => candidate.Renditions)
            .SingleOrDefaultAsync(candidate => candidate.Id == completed.VideoId, cancellationToken);
        if (video is null)
        {
            video = new FeedVideo
            {
                Id = completed.VideoId,
                CreatedAtUtc = now
            };
            dbContext.Videos.Add(video);
        }
        else
        {
            dbContext.Renditions.RemoveRange(video.Renditions);
        }

        video.AvailableAtUtc = completed.OccurredAtUtc;
        video.HasCompletion = true;
        video.HasHls = completed.EventVersion == 2;
        video.SortKey = FeedSortKey.Create(completed.OccurredAtUtc, completed.VideoId);
        video.UpdatedAtUtc = now;
        video.Renditions = completed.Renditions.Select(rendition => new FeedRendition
        {
            VideoId = completed.VideoId,
            Tier = rendition.Tier,
            Width = rendition.Width,
            Height = rendition.Height,
            VideoCodec = rendition.VideoCodec,
            AudioCodec = rendition.AudioCodec,
            ContentType = rendition.ContentType,
            Bucket = rendition.Bucket,
            ObjectKey = rendition.ObjectKey,
            Etag = rendition.Etag.Trim('"'),
            SizeBytes = rendition.SizeBytes
        }).ToList();
    }

    private ParsedEvent Parse(ConsumedEnvelope envelope)
    {
        try
        {
            if (string.Equals(envelope.Topic, topics.UploadedTopic, StringComparison.Ordinal))
            {
                var uploaded = JsonSerializer.Deserialize<VideoUploadedV1>(envelope.Payload, SerializerOptions);
                return Validate(uploaded)
                    ? new ParsedEvent(uploaded!.EventId, uploaded, null, null)
                    : ParsedEvent.Rejected(uploaded?.EventId, "invalid_uploaded_event");
            }

            if (string.Equals(envelope.Topic, topics.CompletedTopic, StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(envelope.Payload);
                var version = document.RootElement.TryGetProperty("eventVersion", out var versionElement) ? versionElement.GetInt32() : 0;
                if (version == 1)
                {
                    var completed = JsonSerializer.Deserialize<VideoTranscodingCompletedV1>(envelope.Payload, SerializerOptions);
                    return Validate(completed) ? new ParsedEvent(completed!.EventId, null, completed, null) : ParsedEvent.Rejected(completed?.EventId, "invalid_completed_event");
                }
                if (version == 2)
                {
                    var completedV2 = JsonSerializer.Deserialize<VideoTranscodingCompletedV2>(envelope.Payload, SerializerOptions);
                    if (!Validate(completedV2)) return ParsedEvent.Rejected(completedV2?.EventId, "invalid_completed_event");
                    var compatible = new VideoTranscodingCompletedV1(completedV2!.EventId,completedV2.EventType,2,completedV2.OccurredAtUtc,completedV2.CausationEventId,completedV2.VideoId,completedV2.SourceBucket,completedV2.SourceObjectKey,completedV2.SourceEtag,completedV2.Renditions,completedV2.CorrelationId);
                    return new ParsedEvent(compatible.EventId,null,compatible,null);
                }
                return ParsedEvent.Rejected(null, "unsupported_completed_version");
            }

            return ParsedEvent.Rejected(null, "unexpected_topic");
        }
        catch (JsonException)
        {
            return ParsedEvent.Rejected(null, "malformed_json");
        }
    }

    private static bool Validate(VideoUploadedV1? uploaded) =>
        uploaded is not null &&
        uploaded.EventId != Guid.Empty &&
        uploaded.VideoId != Guid.Empty &&
        uploaded.EventType == VideoUploadedV1.Type &&
        uploaded.EventVersion == VideoUploadedV1.Version &&
        !string.IsNullOrWhiteSpace(uploaded.Title) &&
        uploaded.Title.Trim().Length <= 200 &&
        (uploaded.Description?.Trim().Length ?? 0) <= 5_000 &&
        uploaded.Hashtags is not null &&
        uploaded.Hashtags.Count <= 10;

    private bool Validate(VideoTranscodingCompletedV1? completed)
    {
        if (completed is null ||
            completed.EventId == Guid.Empty ||
            completed.VideoId == Guid.Empty ||
            completed.EventType != VideoTranscodingCompletedV1.Type ||
            completed.EventVersion is not (1 or 2) ||
            completed.Renditions is null ||
            completed.Renditions.Count == 0 ||
            completed.Renditions.Select(rendition => rendition.Tier).Distinct(StringComparer.Ordinal).Count() !=
                completed.Renditions.Count)
        {
            return false;
        }

        var requiredPrefix = $"videos/{completed.VideoId:N}/";
        return completed.Renditions.All(rendition =>
            rendition.Width > 0 &&
            rendition.Height > 0 &&
            rendition.SizeBytes > 0 &&
            !string.IsNullOrWhiteSpace(rendition.Tier) &&
            !string.IsNullOrWhiteSpace(rendition.VideoCodec) &&
            !string.IsNullOrWhiteSpace(rendition.ContentType) &&
            string.Equals(rendition.Bucket, storage.RenditionsBucket, StringComparison.Ordinal) &&
            rendition.ObjectKey.StartsWith(requiredPrefix, StringComparison.Ordinal));
    }

    private bool Validate(VideoTranscodingCompletedV2? completed) => completed is not null && completed.HlsPackage is not null &&
        string.Equals(completed.HlsPackage.Bucket, storage.RenditionsBucket, StringComparison.Ordinal) &&
        completed.HlsPackage.AssetPrefix == $"videos/{completed.VideoId:N}/hls/" &&
        completed.HlsPackage.MasterPlaylistObjectKey == completed.HlsPackage.AssetPrefix + "master.m3u8" &&
        completed.HlsPackage.Variants is not null && completed.HlsPackage.Variants.Count > 0 &&
        completed.HlsPackage.Variants.All(variant => variant.PlaylistObjectKey == completed.HlsPackage.AssetPrefix + variant.Tier + "/index.m3u8") &&
        Validate(new VideoTranscodingCompletedV1(completed.EventId,completed.EventType,completed.EventVersion,completed.OccurredAtUtc,completed.CausationEventId,completed.VideoId,completed.SourceBucket,completed.SourceObjectKey,completed.SourceEtag,completed.Renditions,completed.CorrelationId));

    private ConsumedKafkaMessage CreateConsumed(
        ConsumedEnvelope envelope,
        Guid? eventId,
        string? rejectionCode) => new()
        {
            Topic = envelope.Topic,
            Partition = envelope.Partition,
            Offset = envelope.Offset,
            EventId = eventId,
            ConsumedAtUtc = timeProvider.GetUtcNow(),
            RejectionCode = rejectionCode
        };

    private sealed record ParsedEvent(
        Guid? EventId,
        VideoUploadedV1? Uploaded,
        VideoTranscodingCompletedV1? Completed,
        string? RejectionCode)
    {
        public static ParsedEvent Rejected(Guid? eventId, string rejectionCode) =>
            new(eventId, null, null, rejectionCode);
    }
}
