using System.Globalization;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Builds deterministic, date-partitioned, unique MinIO keys for source videos.
/// </summary>
public sealed class ObjectKeyFactory
{
    /// <summary>Creates an object key from the server-generated video ID, timestamp, and extension.</summary>
    /// <param name="videoId">The server-generated video identifier.</param>
    /// <param name="uploadedAtUtc">The server-generated UTC upload time.</param>
    /// <param name="extension">The validated lowercase file extension.</param>
    /// <returns>A key below the <c>sources/yyyy/MM/dd</c> prefix.</returns>
    public string Create(Guid videoId, DateTimeOffset uploadedAtUtc, string extension)
    {
        var timestamp = uploadedAtUtc.UtcDateTime.ToString(
            "yyyy/MM/dd/yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);
        return $"sources/{timestamp}-{videoId:N}{extension}";
    }
}
