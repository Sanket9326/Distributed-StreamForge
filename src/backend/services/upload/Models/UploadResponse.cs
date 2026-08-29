namespace StreamForge.Upload.Api.Models;

/// <summary>
/// Describes the durable queued-video receipt returned by the upload API.
/// </summary>
public sealed record UploadResponse(
    Guid Id,
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags,
    string Status,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAtUtc,
    string CorrelationId);
