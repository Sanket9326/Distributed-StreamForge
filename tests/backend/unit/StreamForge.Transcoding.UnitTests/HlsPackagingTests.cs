using StreamForge.Transcoding.Worker.Media;
namespace StreamForge.Transcoding.UnitTests;
public sealed class HlsPackagingTests
{
    [Fact] public void BuildArguments_RemuxesFourSecondFmp4WithoutEncoding(){var args=FfmpegHlsPackager.BuildArguments("in.mp4","out/index.m3u8",4);Pair(args,"-c","copy");Pair(args,"-hls_time","4");Pair(args,"-hls_segment_type","fmp4");Pair(args,"-hls_playlist_type","vod");}
    [Fact] public void Master_IsOrderedAndContainsMeasuredBandwidth(){var low=new StreamForge.Transcoding.Worker.Services.ProcessedHlsVariant("360p",640,360,30,"h264","aac","avc1.4d401f,mp4a.40.2",800000,700000,"x","e",2,10);var high=low with{Tier="720p",Width=1280,Height=720,BandwidthBitsPerSecond=3000000};var text=new HlsManifestBuilder().BuildMaster([high,low]);Assert.True(text.IndexOf("360p/index",StringComparison.Ordinal)<text.IndexOf("720p/index",StringComparison.Ordinal));Assert.Contains("BANDWIDTH=800000,AVERAGE-BANDWIDTH=700000",text);}
    [Fact] public void Keys_AreDeterministic(){var id=Guid.Parse("e2c1bb10-4340-452f-9fc6-a68cf4b12457");var keys=new HlsObjectKeyFactory();Assert.Equal("videos/e2c1bb104340452f9fc6a68cf4b12457/hls/master.m3u8",keys.Master(id));Assert.Equal("videos/e2c1bb104340452f9fc6a68cf4b12457/hls/720p/segment-00000.m4s",keys.Asset(id,"720p","segment-00000.m4s"));}
    private static void Pair(IReadOnlyList<string> values,string key,string value){var index=values.ToList().IndexOf(key);Assert.True(index>=0);Assert.Equal(value,values[index+1]);}
}
