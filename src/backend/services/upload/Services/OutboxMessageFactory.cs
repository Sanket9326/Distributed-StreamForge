using System.Text.Json;
using StreamForge.Upload.Api.Data.Entities;
using StreamForge.Upload.Api.Models;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Serializes a video-uploaded event into the durable PostgreSQL outbox representation.
/// </summary>
public sealed class OutboxMessageFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Creates a pending outbox row for a versioned video-uploaded event.</summary>
    /// <param name="videoUploaded">The event contract to serialize.</param>
    /// <param name="topic">The Kafka destination topic.</param>
    /// <returns>A pending outbox entity keyed by the event ID.</returns>
    public OutboxMessage Create(VideoUploadedV1 videoUploaded, string topic) =>
        new()
        {
            Id = videoUploaded.EventId,
            VideoId = videoUploaded.VideoId,
            Type = videoUploaded.EventType,
            Version = videoUploaded.EventVersion,
            Topic = topic,
            PartitionKey = videoUploaded.VideoId.ToString("D"),
            Payload = JsonSerializer.Serialize(videoUploaded, SerializerOptions),
            OccurredAtUtc = videoUploaded.OccurredAtUtc,
            NextAttemptAtUtc = videoUploaded.OccurredAtUtc
        };
}
