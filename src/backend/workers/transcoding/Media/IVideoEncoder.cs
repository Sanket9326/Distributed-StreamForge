namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Encodes one selected rendition into an MP4 file.</summary>
public interface IVideoEncoder
{
    Task EncodeAsync(
        string inputPath,
        string outputPath,
        RenditionDefinition rendition,
        bool hasAudio,
        CancellationToken cancellationToken);
}
