namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Reads media stream properties using ffprobe.</summary>
public interface IMediaProbe
{
    Task<MediaInfo> ProbeSourceAsync(string filePath, CancellationToken cancellationToken);

    Task<MediaInfo> ProbeOutputAsync(string filePath, CancellationToken cancellationToken);

    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}

public sealed record MediaInfo(
    int Width,
    int Height,
    string VideoCodec,
    bool HasAudio,
    string? AudioCodec,
    TimeSpan Duration);
