namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Validates generated media while allowing harmless display-aspect rounding.</summary>
public sealed class GeneratedMediaValidator
{
    private const int DimensionTolerancePixels = 1;

    public void Validate(
        MediaInfo generated,
        MediaInfo source,
        RenditionDefinition expected)
    {
        var dimensionsMatch =
            Math.Abs(generated.Width - expected.Width) <= DimensionTolerancePixels &&
            Math.Abs(generated.Height - expected.Height) <= DimensionTolerancePixels;
        var videoCodecMatches = string.Equals(
            generated.VideoCodec,
            "h264",
            StringComparison.OrdinalIgnoreCase);
        var audioCodecMatches = !source.HasAudio || string.Equals(
            generated.AudioCodec,
            "aac",
            StringComparison.OrdinalIgnoreCase);

        if (!dimensionsMatch || !videoCodecMatches || !audioCodecMatches)
        {
            throw new PermanentTranscodingException(
                "generated_media_invalid",
                $"Generated rendition {expected.Tier} did not match its required media profile. " +
                $"Expected approximately {expected.Width}x{expected.Height} H.264" +
                $"{(source.HasAudio ? "/AAC" : string.Empty)}; detected " +
                $"{generated.Width}x{generated.Height} {generated.VideoCodec}" +
                $"{(generated.AudioCodec is null ? string.Empty : $"/{generated.AudioCodec}")}.");
        }
    }
}
