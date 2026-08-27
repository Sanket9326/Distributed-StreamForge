namespace StreamForge.Upload.Api.Services;

public sealed class UploadRequestException(int statusCode, string title, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;
}
