using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace StreamForge.Engagement.Api.Services;

public sealed class SubscriptionCursorCodec
{
    public string Encode(Guid userId, bool incoming, string sortKey) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes($"{userId:N}:{(incoming ? 1 : 0)}:{sortKey}"));

    public string? Decode(Guid userId, bool incoming, string? cursor)
    {
        if (cursor is null) return null;
        try
        {
            if (cursor.Length > 200) throw new FormatException();
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            var prefix = $"{userId:N}:{(incoming ? 1 : 0)}:";
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) throw new FormatException();
            var key = text[prefix.Length..];
            if (key.Length != 52 || key[19] != ':' ||
                !long.TryParse(key[..19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ||
                !Guid.TryParseExact(key[20..], "N", out _)) throw new FormatException();
            return key;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new EngagementRequestException(400, "Invalid cursor", "Use the next cursor returned for this list.");
        }
    }

    public static string SortTime(DateTimeOffset date) => date.UtcTicks.ToString("D19", CultureInfo.InvariantCulture);
}
