namespace StreamForge.Engagement.Api.Services;

public sealed class WatchHistoryCursorCodec
{
    private readonly SubscriptionCursorCodec codec = new();
    public string Encode(Guid user, string sortKey) => "wh1." + codec.Encode(user, false, sortKey);
    public string? Decode(Guid user, string? cursor)
    {
        if (cursor is null) return null;
        if (!cursor.StartsWith("wh1.", StringComparison.Ordinal))
            throw new EngagementRequestException(400, "Invalid cursor", "Use a cursor returned for this history list.");
        return codec.Decode(user, false, cursor[4..]);
    }
}
