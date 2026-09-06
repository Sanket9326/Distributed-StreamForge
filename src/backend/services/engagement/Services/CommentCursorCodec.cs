using System.Text;
using System.Text.Json;

namespace StreamForge.Engagement.Api.Services;

public sealed class CommentCursorCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Encode(DateTimeOffset createdAtUtc, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Cursor(createdAtUtc, id), Json)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public (DateTimeOffset CreatedAtUtc, Guid Id) Decode(string value)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            var cursor = JsonSerializer.Deserialize<Cursor>(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)), Json);
            if (cursor is null || cursor.Id == Guid.Empty) throw new JsonException();
            return (cursor.CreatedAtUtc, cursor.Id);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new EngagementRequestException(400, "Invalid comment cursor", "The comment cursor is invalid.");
        }
    }

    private sealed record Cursor(DateTimeOffset CreatedAtUtc, Guid Id);
}
