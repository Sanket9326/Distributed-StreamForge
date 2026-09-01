using System.Globalization;

namespace StreamForge.Feed.Api.Services;

public static class FeedSortKey
{
    public static string Create(DateTimeOffset availableAtUtc, Guid videoId) =>
        $"{availableAtUtc.UtcDateTime.Ticks.ToString("D19", CultureInfo.InvariantCulture)}-{videoId:N}";
}
