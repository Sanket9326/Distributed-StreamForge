using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Parses ffprobe JSON into the media properties needed by the rendition pipeline.</summary>
public sealed class FfprobeMediaProbe(
    IProcessRunner processRunner,
    IOptions<MediaToolOptions> options) : IMediaProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private readonly MediaToolOptions mediaTools = options.Value;

    public Task<MediaInfo> ProbeSourceAsync(string filePath, CancellationToken cancellationToken) =>
        ProbeAsync(filePath, "source_media_invalid", cancellationToken);

    public Task<MediaInfo> ProbeOutputAsync(string filePath, CancellationToken cancellationToken) =>
        ProbeAsync(filePath, "generated_media_invalid", cancellationToken);

    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            mediaTools.FfprobePath,
            ["-version"],
            TimeSpan.FromSeconds(5),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new TransientTranscodingException("ffprobe_unavailable", "ffprobe did not report a valid version.");
        }
    }

    private async Task<MediaInfo> ProbeAsync(
        string filePath,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            mediaTools.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", filePath],
            ProbeTimeout,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new PermanentTranscodingException(failureCode, "The media file could not be probed.");
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var streams = document.RootElement.GetProperty("streams");
            var video = streams.EnumerateArray().FirstOrDefault(stream =>
                string.Equals(GetString(stream, "codec_type"), "video", StringComparison.Ordinal));
            if (video.ValueKind == JsonValueKind.Undefined)
            {
                throw new PermanentTranscodingException(failureCode, "The media file contains no video stream.");
            }

            var codedWidth = video.GetProperty("width").GetInt32();
            var codedHeight = video.GetProperty("height").GetInt32();
            var displayAspectRatio = ReadDisplayAspectRatio(video, codedWidth, codedHeight);
            var rotation = ReadRotation(video);
            var rotated = Math.Abs(rotation) % 180 == 90;
            var height = rotated ? codedWidth : codedHeight;
            var width = (int)Math.Round(
                height * (rotated ? 1 / displayAspectRatio : displayAspectRatio),
                MidpointRounding.AwayFromZero);

            var audio = streams.EnumerateArray().FirstOrDefault(stream =>
                string.Equals(GetString(stream, "codec_type"), "audio", StringComparison.Ordinal));
            var duration = ReadDuration(video, document.RootElement);
            if (width <= 0 || height <= 0 || duration <= TimeSpan.Zero)
            {
                throw new PermanentTranscodingException(failureCode, "The media file has invalid dimensions or duration.");
            }

            return new MediaInfo(
                width,
                height,
                GetString(video, "codec_name") ?? "unknown",
                audio.ValueKind != JsonValueKind.Undefined,
                audio.ValueKind == JsonValueKind.Undefined ? null : GetString(audio, "codec_name"),
                duration);
        }
        catch (PermanentTranscodingException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new PermanentTranscodingException(failureCode, "The media probe returned invalid metadata.", exception);
        }
    }

    private static int ReadRotation(JsonElement video)
    {
        if (video.TryGetProperty("tags", out var tags) &&
            tags.TryGetProperty("rotate", out var rotateTag) &&
            int.TryParse(rotateTag.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tagRotation))
        {
            return tagRotation;
        }

        if (video.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) && rotation.TryGetInt32(out var value))
                {
                    return value;
                }
            }
        }

        return 0;
    }

    private static double ReadDisplayAspectRatio(JsonElement video, int width, int height)
    {
        var ratio = GetString(video, "display_aspect_ratio");
        if (ratio is not null)
        {
            var parts = ratio.Split(':', 2);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
                numerator > 0 && denominator > 0)
            {
                return numerator / denominator;
            }
        }

        return width > 0 && height > 0 ? width / (double)height : 0;
    }

    private static TimeSpan ReadDuration(JsonElement video, JsonElement root)
    {
        var durationText = GetString(video, "duration");
        if (durationText is null && root.TryGetProperty("format", out var format))
        {
            durationText = GetString(format, "duration");
        }

        return double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
}
