using StreamForge.Feed.Api.Data.Entities;

namespace StreamForge.Feed.Api.Services;

public sealed record SignedPlaybackUrl(string Url, DateTimeOffset ExpiresAtUtc);

public interface IPlaybackUrlSigner
{
    Task<SignedPlaybackUrl> SignAsync(FeedRendition rendition, CancellationToken cancellationToken);
}
