namespace StreamForge.Transcoding.Worker.Media;

public interface IHlsPackager
{
    Task PackageAsync(string inputPath, string outputDirectory, CancellationToken cancellationToken);
}
