using System.Diagnostics.Metrics;
namespace StreamForge.Playback.Api.Services;
public sealed class PlaybackTelemetry:IDisposable
{
    private readonly Meter meter=new("StreamForge.Playback","1.0.0");
    public Counter<long> ManifestRequests{get;} public Counter<long> SigningOperations{get;} public Counter<long> MissingProjections{get;} public Counter<long> InvalidManifests{get;} public Counter<long> StorageFailures{get;}
    public PlaybackTelemetry(){ManifestRequests=meter.CreateCounter<long>("playback.manifest.requests");SigningOperations=meter.CreateCounter<long>("playback.signing.operations");MissingProjections=meter.CreateCounter<long>("playback.projections.missing");InvalidManifests=meter.CreateCounter<long>("playback.manifests.invalid");StorageFailures=meter.CreateCounter<long>("playback.storage.failures");}
    public void Dispose()=>meter.Dispose();
}
