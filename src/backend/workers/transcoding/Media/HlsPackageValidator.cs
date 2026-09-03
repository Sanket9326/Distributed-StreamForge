using System.Globalization;

namespace StreamForge.Transcoding.Worker.Media;

public sealed record ValidatedHlsVariant(int SegmentCount, long SizeBytes, long BandwidthBitsPerSecond, long AverageBandwidthBitsPerSecond, TimeSpan Duration);

public sealed class HlsPackageValidator
{
    public ValidatedHlsVariant Validate(string directory)
    {
        var playlist = Path.Combine(directory, "index.m3u8");
        if (!File.Exists(playlist)) throw Invalid("Variant playlist is missing.");
        var lines = File.ReadAllLines(playlist).Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        if (lines.FirstOrDefault() != "#EXTM3U" || !lines.Contains("#EXT-X-ENDLIST") || !lines.Contains("#EXT-X-INDEPENDENT-SEGMENTS"))
            throw Invalid("Variant playlist is not a complete independent VOD playlist.");
        var initLine = lines.SingleOrDefault(line => line.StartsWith("#EXT-X-MAP:URI=", StringComparison.Ordinal));
        var initName = ExtractQuotedUri(initLine);
        ValidateLocalName(initName, ".mp4");
        if (!File.Exists(Path.Combine(directory, initName))) throw Invalid("Initialization segment is missing.");
        var durations = new List<double>();
        var segmentNames = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var text = line[8..].TrimEnd(',');
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0) throw Invalid("Segment duration is invalid.");
                durations.Add(duration);
            }
            else if (!line.StartsWith('#')) segmentNames.Add(line);
        }
        if (durations.Count == 0 || durations.Count != segmentNames.Count) throw Invalid("Segment list is invalid.");
        foreach (var name in segmentNames) { ValidateLocalName(name, ".m4s"); if (!File.Exists(Path.Combine(directory, name))) throw Invalid("A media segment is missing."); }
        var segmentSizes = segmentNames.Select(name => new FileInfo(Path.Combine(directory, name)).Length).ToArray();
        var durationSeconds = durations.Sum();
        var mediaBytes = segmentSizes.Sum();
        var totalBytes = mediaBytes + new FileInfo(Path.Combine(directory, initName)).Length + new FileInfo(playlist).Length;
        var peak = segmentSizes.Zip(durations).Max(pair => (long)Math.Ceiling(pair.First * 8d / pair.Second));
        var average = (long)Math.Ceiling(totalBytes * 8d / durationSeconds);
        return new ValidatedHlsVariant(segmentNames.Count, totalBytes, peak, average, TimeSpan.FromSeconds(durationSeconds));
    }

    private static string ExtractQuotedUri(string? line)
    {
        if (line is null) throw Invalid("Initialization segment declaration is missing.");
        var first = line.IndexOf('"'); var last = line.LastIndexOf('"');
        if (first < 0 || last <= first) throw Invalid("Initialization segment URI is invalid.");
        return line[(first + 1)..last];
    }
    private static void ValidateLocalName(string value, string extension)
    {
        if (value != Path.GetFileName(value) || value.Contains("..", StringComparison.Ordinal) || !value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw Invalid("Playlist URI is unsafe.");
    }
    private static PermanentTranscodingException Invalid(string message) => new("hls_validation_failed", message);
}
