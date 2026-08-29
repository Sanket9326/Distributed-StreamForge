namespace StreamForge.Upload.Api.Models;

/// <summary>
/// Defines version 1 of the event published after source video ingestion becomes durable.
/// </summary>
public sealed record VideoUploadedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid VideoId,
    string Bucket,
    string ObjectKey,
    string Etag,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags,
    Guid? OwnerId,
    DateTimeOffset UploadedAtUtc,
    string CorrelationId)
{
    /// <summary>Gets the stable event type name.</summary>
    public const string Type = "video.uploaded";

    /// <summary>Gets the serialized event contract version.</summary>
    public const int Version = 1;
}
