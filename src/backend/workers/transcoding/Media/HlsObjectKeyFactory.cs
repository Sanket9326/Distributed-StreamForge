namespace StreamForge.Transcoding.Worker.Media;

public sealed class HlsObjectKeyFactory
{
    public string Prefix(Guid videoId) => $"videos/{videoId:N}/hls/";
    public string Master(Guid videoId) => $"{Prefix(videoId)}master.m3u8";
    public string Variant(Guid videoId, string tier) => $"{Prefix(videoId)}{tier}/index.m3u8";
    public string Asset(Guid videoId, string tier, string fileName) => $"{Prefix(videoId)}{tier}/{fileName}";
}
