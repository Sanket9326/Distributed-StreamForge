using System.Diagnostics;

namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Executes FFmpeg tools with bounded lifetime and process-tree cancellation.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int MaximumCapturedCharacters = 32_768;

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start media executable '{executable}'.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new TransientTranscodingException(
                "media_tool_unavailable",
                $"Required media executable '{Path.GetFileName(executable)}' is unavailable.",
                exception);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
            var output = await outputTask;
            var error = await errorTask;
            return new ProcessResult(
                process.ExitCode,
                output,
                Truncate(error));
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw new TransientTranscodingException(
                "media_tool_timeout",
                $"Media executable '{Path.GetFileName(executable)}' exceeded its time limit.");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and the kill request.
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaximumCapturedCharacters ? value : value[^MaximumCapturedCharacters..];
}
