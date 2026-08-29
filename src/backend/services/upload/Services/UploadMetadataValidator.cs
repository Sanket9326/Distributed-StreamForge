using StreamForge.Upload.Api.Models;

namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Validates descriptive upload fields and returns their normalized representation.
/// </summary>
public sealed class UploadMetadataValidator
{
    /// <summary>Gets the maximum accepted title length.</summary>
    public const int MaximumTitleLength = 200;

    /// <summary>Gets the maximum accepted description length.</summary>
    public const int MaximumDescriptionLength = 5_000;

    /// <summary>Gets the maximum number of unique normalized hashtags.</summary>
    public const int MaximumHashtagCount = 10;

    /// <summary>Gets the maximum length of one normalized hashtag.</summary>
    public const int MaximumHashtagLength = 50;

    /// <summary>Validates title and description limits and normalizes hashtags.</summary>
    /// <param name="title">The submitted title.</param>
    /// <param name="description">The optional submitted description.</param>
    /// <param name="submittedHashtags">The repeated or comma-separated hashtag values.</param>
    /// <returns>Validated, trimmed metadata with normalized unique hashtags.</returns>
    public UploadMetadata Validate(
        string? title,
        string? description,
        IEnumerable<string> submittedHashtags)
    {
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            throw InvalidMetadata("A title is required.");
        }

        if (normalizedTitle.Length > MaximumTitleLength)
        {
            throw InvalidMetadata($"The title cannot exceed {MaximumTitleLength} characters.");
        }

        var normalizedDescription = description?.Trim();
        if (normalizedDescription?.Length > MaximumDescriptionLength)
        {
            throw InvalidMetadata(
                $"The description cannot exceed {MaximumDescriptionLength} characters.");
        }

        if (string.IsNullOrEmpty(normalizedDescription))
        {
            normalizedDescription = null;
        }

        var hashtags = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var submittedHashtag in submittedHashtags)
        {
            foreach (var part in submittedHashtag.Split(',', StringSplitOptions.TrimEntries))
            {
                var hashtag = part.Trim();
                if (hashtag.StartsWith('#'))
                {
                    hashtag = hashtag[1..].Trim();
                }

                hashtag = hashtag.ToLowerInvariant();
                if (hashtag.Length == 0)
                {
                    continue;
                }

                if (hashtag.Length > MaximumHashtagLength)
                {
                    throw InvalidMetadata(
                        $"Each hashtag cannot exceed {MaximumHashtagLength} characters.");
                }

                if (hashtag.Any(character =>
                        !char.IsLetterOrDigit(character) && character is not '_' and not '-'))
                {
                    throw InvalidMetadata(
                        "Hashtags may contain only letters, numbers, underscores, and hyphens.");
                }

                if (seen.Add(hashtag))
                {
                    hashtags.Add(hashtag);
                }
            }
        }

        if (hashtags.Count > MaximumHashtagCount)
        {
            throw InvalidMetadata($"Supply no more than {MaximumHashtagCount} hashtags.");
        }

        return new UploadMetadata(normalizedTitle, normalizedDescription, hashtags);
    }

    private static UploadRequestException InvalidMetadata(string detail) =>
        new(StatusCodes.Status400BadRequest, "Invalid video metadata", detail);
}
