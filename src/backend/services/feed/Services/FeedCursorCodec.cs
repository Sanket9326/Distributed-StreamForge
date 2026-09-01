using System.Text;
using System.Text.RegularExpressions;

namespace StreamForge.Feed.Api.Services;

public sealed partial class FeedCursorCodec
{
    public string Encode(string sortKey)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(sortKey));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 256)
        {
            throw InvalidCursor();
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            var sortKey = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            if (!SortKeyPattern().IsMatch(sortKey))
            {
                throw InvalidCursor();
            }

            return sortKey;
        }
        catch (FormatException)
        {
            throw InvalidCursor();
        }
    }

    private static FeedRequestException InvalidCursor() => new(
        StatusCodes.Status400BadRequest,
        "Invalid feed cursor",
        "The feed cursor is malformed or no longer supported.");

    [GeneratedRegex("^[0-9]{19}-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex SortKeyPattern();
}
