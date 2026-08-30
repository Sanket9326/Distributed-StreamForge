using System.Diagnostics.Metrics;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Defines low-cardinality operational metrics for the worker.</summary>
public sealed class TranscodingTelemetry : IDisposable
{
    private readonly Meter meter = new("StreamForge.Transcoding", "1.0.0");

    public TranscodingTelemetry()
    {
        AcceptedJobs = meter.CreateCounter<long>("transcoding.jobs.accepted");
        DeadLetters = meter.CreateCounter<long>("transcoding.messages.dead_lettered");
        CompletedJobs = meter.CreateCounter<long>("transcoding.jobs.completed");
        FailedJobs = meter.CreateCounter<long>("transcoding.jobs.failed");
        RetriedJobs = meter.CreateCounter<long>("transcoding.jobs.retried");
        EncodeDuration = meter.CreateHistogram<double>("transcoding.jobs.duration", "s");
        QueueAge = meter.CreateHistogram<double>("transcoding.jobs.queue_age", "s");
        RenditionSize = meter.CreateHistogram<long>("transcoding.renditions.size", "By");
    }

    public Counter<long> AcceptedJobs { get; }

    public Counter<long> DeadLetters { get; }

    public Counter<long> CompletedJobs { get; }

    public Counter<long> FailedJobs { get; }

    public Counter<long> RetriedJobs { get; }

    public Histogram<double> EncodeDuration { get; }

    public Histogram<double> QueueAge { get; }

    public Histogram<long> RenditionSize { get; }

    public void Dispose() => meter.Dispose();
}
