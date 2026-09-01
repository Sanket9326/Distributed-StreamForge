namespace StreamForge.Feed.Api.Services;

public sealed class StartupGate
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken cancellationToken) => ready.Task.WaitAsync(cancellationToken);

    public void MarkReady() => ready.TrySetResult();
}
