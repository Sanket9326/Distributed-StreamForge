using System.Globalization;
using System.Text;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.Worker.Media;

public sealed class HlsManifestBuilder
{
    public string BuildMaster(IEnumerable<ProcessedHlsVariant> variants)
    {
        var builder = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-INDEPENDENT-SEGMENTS\n");
        foreach (var item in variants.OrderBy(value => value.Height).ThenBy(value => value.Width))
        {
            builder.Append("#EXT-X-STREAM-INF:BANDWIDTH=").Append(item.BandwidthBitsPerSecond)
                .Append(",AVERAGE-BANDWIDTH=").Append(item.AverageBandwidthBitsPerSecond)
                .Append(",RESOLUTION=").Append(item.Width).Append('x').Append(item.Height)
                .Append(",FRAME-RATE=").Append(item.FrameRate.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(",CODECS=\"").Append(item.Codecs).Append("\"\n")
                .Append(item.Tier).Append("/index.m3u8\n");
        }
        return builder.ToString();
    }
}
