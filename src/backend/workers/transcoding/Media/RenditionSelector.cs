namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Selects standard, non-upscaled output tiers for a probed source.</summary>
public sealed class RenditionSelector
{
    private static readonly int[] StandardHeights = [360, 480, 720, 1080];

    public IReadOnlyList<RenditionDefinition> Select(MediaInfo source)
    {
        if (source.Width < 2 || source.Height < 2)
        {
            throw new PermanentTranscodingException("source_dimensions_invalid", "Source dimensions are too small to encode.");
        }

        var selectedHeights = StandardHeights.Where(height => height <= source.Height).ToArray();
        if (selectedHeights.Length == 0)
        {
            selectedHeights = [MakeEven(source.Height)];
        }

        return selectedHeights.Select(height => Create(source, height)).ToArray();
    }

    private static RenditionDefinition Create(MediaInfo source, int height)
    {
        var scaledWidth = source.Width * (height / (double)source.Height);
        var width = Math.Max(
            2,
            (int)(2 * Math.Round(scaledWidth / 2, MidpointRounding.AwayFromZero)));
        var profile = height switch
        {
            <= 360 => (Crf: 23, MaxRate: "800k", BufferSize: "1600k", AudioRate: "128k"),
            <= 480 => (Crf: 23, MaxRate: "1500k", BufferSize: "3000k", AudioRate: "128k"),
            <= 720 => (Crf: 22, MaxRate: "3000k", BufferSize: "6000k", AudioRate: "128k"),
            _ => (Crf: 21, MaxRate: "6000k", BufferSize: "12000k", AudioRate: "128k")
        };
        return new RenditionDefinition(
            $"{height}p",
            width,
            height,
            profile.Crf,
            profile.MaxRate,
            profile.BufferSize,
            profile.AudioRate,
            Math.Min(source.FrameRate <= 0 ? 30 : source.FrameRate, 30));
    }

    private static int MakeEven(int value) => Math.Max(2, value - Math.Abs(value % 2));
}

public sealed record RenditionDefinition(
    string Tier,
    int Width,
    int Height,
    int Crf,
    string MaximumVideoRate,
    string VideoBufferSize,
    string AudioRate,
    double FrameRate = 30);
