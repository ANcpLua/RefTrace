using System.Diagnostics;
using RefTrace.Core;

namespace RefTrace.Build;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

// Own the complete process lifetime, including descendants and both output pipes.
public static class ProcessCapture
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        string directory, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = directory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        using var process = Process.Start(start) ?? throw new InspectionException("tool-unavailable", $"Cannot start {executable}.", executable);
        var capture = process.ReadAllTextAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var (output, error) = await capture.ConfigureAwait(false);
            return new(process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Process exited while cancellation was handled. */ }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await capture.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new InspectionException("timeout", $"{executable} exceeded the {timeout.TotalSeconds:g} second deadline.", executable);
        }
    }
}
