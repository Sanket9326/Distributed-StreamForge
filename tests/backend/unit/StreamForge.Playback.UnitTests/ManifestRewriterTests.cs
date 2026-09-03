using StreamForge.Playback.Api.Data;using StreamForge.Playback.Api.Services;
namespace StreamForge.Playback.UnitTests;
public sealed class ManifestRewriterTests
{
    private static readonly Guid Id=Guid.Parse("e2c1bb10-4340-452f-9fc6-a68cf4b12457");
    [Fact]public void Master_RewritesOnlyProjectedVariants(){var package=Package();var value=new ManifestRewriter().RewriteMaster("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\n720p/index.m3u8\n",package);Assert.Contains($"/api/playback/videos/{Id:D}/variants/720p.m3u8",value);}
    [Theory][InlineData("https://evil.test/a.m4s")][InlineData("../secret.m4s")][InlineData("/root.m4s")]public async Task Variant_RejectsUnsafeUris(string uri){var package=Package();await Assert.ThrowsAsync<InvalidManifestException>(()=>new ManifestRewriter().RewriteVariantAsync($"#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\n{uri}\n#EXT-X-ENDLIST\n",package,package.Variants[0],Task.FromResult));}
    [Fact]public async Task Variant_SignsInitAndSegments(){var package=Package();var signed=new List<string>();var result=await new ManifestRewriter().RewriteVariantAsync("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\nsegment-00000.m4s\n#EXT-X-ENDLIST\n",package,package.Variants[0],key=>{signed.Add(key);return Task.FromResult("https://signed/"+Path.GetFileName(key));});Assert.Equal(2,signed.Count);Assert.Contains("https://signed/init.mp4",result);}
    private static PlaybackPackage Package(){var prefix=$"videos/{Id:N}/hls/";var package=new PlaybackPackage{VideoId=Id,Bucket="streamforge-renditions",AssetPrefix=prefix,MasterPlaylistObjectKey=prefix+"master.m3u8"};package.Variants.Add(new PlaybackVariant{VideoId=Id,Tier="720p",PlaylistObjectKey=prefix+"720p/index.m3u8"});return package;}
}
