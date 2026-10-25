using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RefTrace.Runtime;
using Spectre.Console;

namespace RefTrace.Tests;

[TestClass]
public sealed class RuntimeInspectorTests(TestContext context)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Builder_CorrelatesOutOfOrderModulesAndSeparatesClrInstances()
    {
        var builder = new RuntimeObservationBuilder();
        builder.Module(1, 42, 7, "/one.dll", "");
        builder.Assembly(1, 42, 3, "One, Version=1.0.0.0", true);
        builder.Assembly(2, 42, 4, "Two, Version=2.0.0.0", true);
        builder.CompleteRundown(1);
        var incomplete = builder.Build(99, "target", 100, Start, Start.AddSeconds(1), 0);
        Assert.IsFalse(incomplete.IsConclusive);
        builder.CompleteRundown(2);
        var complete = builder.Build(99, "target", 100, Start, Start.AddSeconds(1), 0);
        Assert.IsTrue(complete.IsConclusive);
        Assert.HasCount(2, complete.Assemblies);
        Assert.AreEqual("/one.dll", complete.Assemblies.Single(a => a.ClrInstanceId == 1).Modules.Single().IlPath);
        Assert.AreEqual(4L, complete.Assemblies.Single(a => a.ClrInstanceId == 2).AppDomainId);
    }

    [TestMethod]
    public void Builder_EmptyLostAndOrphanedObservations_DoNotProveAbsence()
    {
        var builder = new RuntimeObservationBuilder();
        builder.CompleteRundown(0);
        Assert.IsFalse(builder.Build(1, "empty", 2, Start, Start, 0).IsConclusive);
        builder.Assembly(0, 10, 1, "Example", true);
        var lost = builder.Build(1, "target", 2, Start, Start, 5);
        Assert.IsFalse(lost.IsConclusive);
        Assert.AreEqual(5L, lost.EventsLost);
        builder.Module(0, 11, 20, "/orphan.dll", "");
        Assert.IsFalse(builder.Build(1, "target", 2, Start, Start, 0).IsConclusive);
    }

    [TestMethod]
    public void Builder_PreservesLoadAndUnloadObservations()
    {
        var builder = new RuntimeObservationBuilder();
        builder.Assembly(0, 10, 1, "Example", false);
        builder.Assembly(0, 10, 1, "Example", false, unloaded: true);
        var observed = builder.Build(1, "target", 2, Start, Start, 0).Assemblies.Single();
        Assert.IsTrue(observed.LoadObserved);
        Assert.IsTrue(observed.UnloadObserved);
        Assert.IsFalse(observed.SeenInRundown);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Capture_ControlledProcess_EnumeratesExistingAssembliesWithoutObserverDependencies()
    {
        using var observerOutput = new StringWriter();
        AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(observerOutput) }).Write(new Text("observer"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "RefTrace.RuntimeFixture", configuration, "RefTrace.RuntimeFixture.dll"));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(fixture);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start.");
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync(context.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken);
            Assert.IsNotNull(ready);
            Assert.StartsWith("READY ", ready);
            var report = await RuntimeInspector.CaptureAsync(process.Id, TimeSpan.FromSeconds(0.2),
                TimeSpan.FromSeconds(15), context.CancellationToken);
            Assert.IsTrue(report.IsConclusive, string.Join("; ", report.Notes));
            Assert.AreEqual(process.Id, report.ProcessId);
            Assert.AreEqual(Environment.ProcessId, report.ObserverProcessId);
            Assert.AreEqual(0L, report.EventsLost);
            Assert.IsTrue(report.Assemblies.Any(a => a.FullName.StartsWith("RefTrace.Metadata,", StringComparison.Ordinal) && a.SeenInRundown));
            Assert.IsTrue(report.Assemblies.SelectMany(a => a.Modules).Any(m => m.IlPath.EndsWith("RefTrace.RuntimeFixture.dll", StringComparison.Ordinal)));
            Assert.IsFalse(report.Assemblies.Any(a => a.FullName.StartsWith("Spectre.Console,", StringComparison.Ordinal)));
            Assert.IsFalse(report.Assemblies.Any(a => a.FullName.StartsWith("Microsoft.Diagnostics.NETCore.Client,", StringComparison.Ordinal)));
        }
        finally
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { process.Kill(entireProcessTree: true); }
            }
        }
    }
}
