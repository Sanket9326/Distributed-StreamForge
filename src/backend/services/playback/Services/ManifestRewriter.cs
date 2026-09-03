using StreamForge.Playback.Api.Data;
namespace StreamForge.Playback.Api.Services;
public sealed class InvalidManifestException(string message):Exception(message);
public sealed class ManifestRewriter
{
    public string RewriteMaster(string manifest,PlaybackPackage package)
    {
        var known=package.Variants.ToDictionary(x=>Relative(package.AssetPrefix,x.PlaylistObjectKey),x=>x.Tier,StringComparer.Ordinal);
        var lines=Lines(manifest); var output=new List<string>(lines.Length);
        foreach(var line in lines){if(line.Length==0||line[0]=='#'){output.Add(line);continue;}ValidateUri(line);if(!known.TryGetValue(line,out var tier))throw new InvalidManifestException("Master references an unknown variant.");output.Add($"/api/playback/videos/{package.VideoId:D}/variants/{Uri.EscapeDataString(tier)}.m3u8");}
        if(!output.Contains("#EXTM3U")||!output.Any(x=>x.StartsWith("#EXT-X-STREAM-INF:",StringComparison.Ordinal)))throw new InvalidManifestException("Master manifest is invalid.");return string.Join('\n',output)+"\n";
    }
    public async Task<string> RewriteVariantAsync(string manifest,PlaybackPackage package,PlaybackVariant variant,Func<string,Task<string>> sign)
    {
        var directory=variant.PlaylistObjectKey[..(variant.PlaylistObjectKey.LastIndexOf('/')+1)]; var lines=Lines(manifest);var output=new List<string>(lines.Length);
        foreach(var line in lines)
        {
            if(line.StartsWith("#EXT-X-MAP:URI=",StringComparison.Ordinal)){var uri=Quoted(line);if(uri!="init.mp4")throw new InvalidManifestException("Initialization URI is unknown.");var signed=await sign(Resolve(package,directory,uri));output.Add(line.Replace($"\"{uri}\"",$"\"{signed}\"",StringComparison.Ordinal));}
            else if(line.Length>0&&line[0]!='#'){if(!System.Text.RegularExpressions.Regex.IsMatch(line,"^segment-[0-9]{5}\\.m4s$",System.Text.RegularExpressions.RegexOptions.CultureInvariant))throw new InvalidManifestException("Media segment URI is unknown.");output.Add(await sign(Resolve(package,directory,line)));} else output.Add(line);
        }
        if(!output.Contains("#EXTM3U")||!output.Contains("#EXT-X-ENDLIST"))throw new InvalidManifestException("Variant manifest is not VOD.");return string.Join('\n',output)+"\n";
    }
    private static string Resolve(PlaybackPackage package,string directory,string uri){ValidateUri(uri);var key=directory+uri;if(!key.StartsWith(package.AssetPrefix,StringComparison.Ordinal)||key.Contains("..",StringComparison.Ordinal))throw new InvalidManifestException("Asset is outside the projected package.");return key;}
    private static string Relative(string prefix,string key){if(!key.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidManifestException("Variant is outside the package.");return key[prefix.Length..];}
    private static void ValidateUri(string uri){if(string.IsNullOrWhiteSpace(uri)||Uri.TryCreate(uri,UriKind.Absolute,out _)||uri.StartsWith('/')||uri.StartsWith('\\')||uri.Contains("..",StringComparison.Ordinal)||uri.Contains('?')||uri.Contains('#'))throw new InvalidManifestException("Manifest URI is unsafe.");}
    private static string Quoted(string line){var first=line.IndexOf('"');var last=line.LastIndexOf('"');if(first<0||last<=first)throw new InvalidManifestException("Map URI is invalid.");return line[(first+1)..last];}
    private static string[] Lines(string value)=>value.Replace("\r",string.Empty,StringComparison.Ordinal).Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
}
