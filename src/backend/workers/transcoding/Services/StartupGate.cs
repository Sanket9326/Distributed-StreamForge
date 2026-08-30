namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Prevents background loops from running before infrastructure initialization finishes.</summary>
public sealed class StartupGate
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => ready.Task.IsCompletedSuccessfully;

    public Task WaitAsync(CancellationToken cancellationToken) => ready.Task.WaitAsync(cancellationToken);

    public void MarkReady() => ready.TrySetResult();
}
