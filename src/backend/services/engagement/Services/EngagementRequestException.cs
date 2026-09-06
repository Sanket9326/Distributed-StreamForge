namespace StreamForge.Engagement.Api.Services;

public sealed class EngagementRequestException(int statusCode, string title, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}
