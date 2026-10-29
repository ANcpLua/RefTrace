using System.Diagnostics;
using RefTrace.Build;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class ProcessCaptureTests(TestContext context)
{
    [TestMethod]
    public async Task Run_PreCancelledDoesNotTryToLaunchAnExecutable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ProcessCapture.RunAsync(
            "no-such-executable", [], context.TestTempDirectory!, TimeSpan.FromSeconds(1), cancelled.Token));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [DoNotParallelize] // Shares the runtime's process-tree termination and reaping machinery.
    [Timeout(25000, CooperativeCancellation = true)]
    public async Task Run_CancellationAndTimeoutRemainDistinctAndTerminateTheChild(bool cancelCaller)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "RefTrace.RuntimeFixture", configuration,
            "RefTrace.RuntimeFixture" + (OperatingSystem.IsWindows() ? ".exe" : "")));
        var pidFile = Path.Combine(context.TestTempDirectory!, "child.pid");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var run = ProcessCapture.RunAsync(executable, ["--wait-child", pidFile], context.TestTempDirectory!,
            TimeSpan.FromSeconds(10), cancellation.Token);
        try
        {
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            ready.CancelAfter(TimeSpan.FromSeconds(8));
            while (!File.Exists(pidFile)) await Task.Delay(20, ready.Token);
            using var process = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile, context.CancellationToken)));
            if (cancelCaller)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => run);
            }
            else
            {
                var error = await Assert.ThrowsExactlyAsync<InspectionException>(() => run);
                Assert.AreEqual("timeout", error.Code);
                Assert.AreEqual(executable, error.Input);
            }
            await process.WaitForExitAsync(context.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            Assert.IsTrue(process.HasExited);
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch (OperationCanceledException) { }
            catch (InspectionException) { }
        }
    }
}
