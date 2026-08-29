namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Represents a client-correctable upload request error and its HTTP status mapping.
/// </summary>
public sealed class UploadRequestException(int statusCode, string title, string message)
    : Exception(message)
{
    /// <summary>Gets the HTTP status code returned for the request error.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>Gets the Problem Details title returned to the caller.</summary>
    public string Title { get; } = title;
}
