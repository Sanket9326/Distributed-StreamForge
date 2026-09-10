using System.Diagnostics.Metrics;

namespace StreamForge.Search.Api.Services;

public sealed class SearchTelemetry : IDisposable
{
    private readonly Meter meter = new("StreamForge.Search", "1.0.0");
    private readonly Counter<long> failureCounter;

    public SearchTelemetry()
    {
        failureCounter = meter.CreateCounter<long>("search.indexing.failures");
    }

    public void RecordFailure(string reason) =>
        failureCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void Dispose() => meter.Dispose();
}
