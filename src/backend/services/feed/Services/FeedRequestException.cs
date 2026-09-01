namespace StreamForge.Feed.Api.Services;

public sealed class FeedRequestException(int statusCode, string title, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;
}
