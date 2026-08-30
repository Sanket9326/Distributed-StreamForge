using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.UnitTests;

public sealed class FfprobeMediaProbeTests
{
    [Fact]
    public async Task ProbeSourceAsync_AppliesDisplayAspectRatioAndRotation()
    {
        const string probeJson = """
            {
              "streams": [
                {
                  "codec_type": "video",
                  "codec_name": "h264",
                  "width": 1920,
                  "height": 1080,
                  "display_aspect_ratio": "16:9",
                  "side_data_list": [{ "rotation": 90 }]
                },
                {
                  "codec_type": "audio",
                  "codec_name": "aac"
                }
              ],
              "format": { "duration": "12.5" }
            }
            """;
        var probe = new FfprobeMediaProbe(
            new StubProcessRunner(new ProcessResult(0, probeJson, string.Empty)),
            Options.Create(new MediaToolOptions()));

        var result = await probe.ProbeSourceAsync("source.mp4", CancellationToken.None);

        Assert.Equal(1080, result.Width);
        Assert.Equal(1920, result.Height);
        Assert.Equal("h264", result.VideoCodec);
        Assert.True(result.HasAudio);
        Assert.Equal("aac", result.AudioCodec);
        Assert.Equal(TimeSpan.FromSeconds(12.5), result.Duration);
    }

    private sealed class StubProcessRunner(ProcessResult result) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
