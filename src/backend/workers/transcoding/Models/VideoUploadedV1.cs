namespace StreamForge.Transcoding.Worker.Models;

/// <summary>Mirrors the public Upload event without referencing Upload implementation code.</summary>
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
    public const string Type = "video.uploaded";

    public const int Version = 1;
}
