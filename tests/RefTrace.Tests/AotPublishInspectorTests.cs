using System.Diagnostics;
using RefTrace.Build;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class AotPublishInspectorTests(TestContext context)
{
    [TestMethod]
    public void ParseDiagnostics_PreservesCodesSeveritiesAndFullEvidence()
    {
        var report = AotPublishInspector.ParseDiagnostics("""
            Starting publish...
            file.cs(1,2): warning IL2026: Metadata may be removed. [App.csproj]
            Dependency.dll : error IL3053: AOT analysis warnings. [App.csproj]
            file.cs(1,2): warning IL2026: Metadata may be removed. [App.csproj]
            App.csproj : error NETSDK1207: Unsupported target.
            """);
        Assert.HasCount(3, report);
        Assert.AreEqual("IL2026", report[0].Code);
        Assert.AreEqual("warning", report[0].Severity);
        Assert.Contains("file.cs(1,2)", report[0].Text);
        Assert.AreEqual("IL3053", report[1].Code);
        Assert.AreEqual("NETSDK1207", report[2].Code);
        Assert.IsEmpty(AotPublishInspector.ParseDiagnostics("Build succeeded."));
    }

    [TestMethod]
    public async Task Run_InvalidProject_DoesNotStartAProcess()
    {
        await Assert.ThrowsExactlyAsync<InspectionException>(() => AotPublishInspector.RunAsync(
            Path.Combine(context.TestTempDirectory!, "missing.csproj"), "osx-arm64", context.TestTempDirectory!,
            TimeSpan.FromSeconds(1), cancellationToken: context.CancellationToken));
    }

    [TestMethod]
    public async Task Run_PreCancelled_DoesNotCreateOutput()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var output = Path.Combine(context.TestTempDirectory!, "not-created");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => AotPublishInspector.RunAsync(
            "unused.csproj", "osx-arm64", output, TimeSpan.FromSeconds(1), cancellationToken: cancellation.Token));
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    [DataRow("success", 0, "succeeded")]
    [DataRow("failure", 7, "failed")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Run_CapturesBothLargeStreamsAndPreservesExitStatus(string name, int exitCode, string outcome)
    {
        var project = CreateFixtureProject(name);
        var report = await AotPublishInspector.RunAsync(project, "osx-arm64",
            Path.Combine(context.TestTempDirectory!, "reports"), TimeSpan.FromSeconds(20),
            FixtureExecutable(), context.CancellationToken);

        Assert.AreEqual(exitCode, report.ExitCode);
        Assert.AreEqual(outcome, report.Outcome);
        Assert.AreEqual("11.0.100-fixture", report.SdkVersion);
        Assert.IsTrue(report.Evaluation.Succeeded);
        Assert.AreEqual("net11.0", report.Evaluation.Properties["TargetFramework"]);
        var text = await File.ReadAllTextAsync(report.LogPath, context.CancellationToken);
        Assert.Contains(new string('O', 131072), text);
        Assert.Contains(new string('E', 131072), text);
        Assert.Contains($"PROJECT {project}", text);
        Assert.Contains($"DIRECTORY {Path.GetDirectoryName(project)}", text);
        Assert.Contains("stdout Grüße 🚀", text);
        Assert.Contains("stderr Grüße 🚀", text);
    }

    [TestMethod]
    [DataRow(true, "cancelled")]
    [DataRow(false, "timed-out")]
    [DoNotParallelize] // RC3 macOS process reaping can stall concurrent process-tree termination.
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Run_CancellationOrDeadline_TerminatesTheProcessTree(bool cancelCaller, string outcome)
    {
        var project = CreateFixtureProject("wait");
        var directory = Path.GetDirectoryName(project)!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var run = AotPublishInspector.RunAsync(project, "osx-arm64",
            Path.Combine(context.TestTempDirectory!, "reports"), TimeSpan.FromSeconds(10),
            FixtureExecutable(), cancellation.Token);
        try
        {
            var childPidFile = Path.Combine(directory, "child.pid");
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            readyTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            while (!File.Exists(childPidFile))
                await Task.Delay(20, readyTimeout.Token);
            var childId = int.Parse(await File.ReadAllTextAsync(childPidFile, context.CancellationToken));
            using var child = Process.GetProcessById(childId);

            if (cancelCaller) await cancellation.CancelAsync();
            var report = await run;
            await child.WaitForExitAsync(context.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            Assert.IsTrue(child.HasExited);
            Assert.AreEqual(outcome, report.Outcome);
            Assert.IsTrue(File.Exists(report.LogPath));
        }
        finally
        {
            await cancellation.CancelAsync();
            KillFixtureIfRunning(Path.Combine(directory, "parent.pid"));
            KillFixtureIfRunning(Path.Combine(directory, "child.pid"));
            await run.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    [TestMethod]
    [DataRow(false, "incomplete-output")]
    [DataRow(true, "cancelled")]
    [DoNotParallelize] // Cleanup terminates the fixture process tree.
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Run_OpenInheritedPipeAfterExit_ReportsDrainDeadlineOrCallerCancellation(bool cancelCaller, string outcome)
    {
        var project = CreateFixtureProject("drain");
        var directory = Path.GetDirectoryName(project)!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var run = AotPublishInspector.RunAsync(project, "osx-arm64",
            Path.Combine(context.TestTempDirectory!, "reports"), TimeSpan.FromSeconds(20),
            FixtureExecutable(), cancellation.Token);
        try
        {
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            ready.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(directory, "child.pid"))) await Task.Delay(20, ready.Token);
            var parentId = int.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "parent.pid"), ready.Token));
            try
            {
                using var parent = Process.GetProcessById(parentId);
                await parent.WaitForExitAsync(ready.Token);
            }
            catch (ArgumentException) { /* The parent exited before its PID was opened. */ }

            Assert.IsFalse(run.IsCompleted, "The child still holds the inherited output pipes open.");
            if (cancelCaller) await cancellation.CancelAsync();
            var report = await run;

            Assert.AreEqual(0, report.ExitCode);
            Assert.AreEqual(outcome, report.Outcome);
            Assert.AreEqual(cancelCaller ? "Output capture was cancelled." : "Output capture did not complete.",
                await File.ReadAllTextAsync(report.LogPath, context.CancellationToken));
        }
        finally
        {
            await cancellation.CancelAsync();
            KillFixtureIfRunning(Path.Combine(directory, "parent.pid"));
            KillFixtureIfRunning(Path.Combine(directory, "child.pid"));
            await run.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private string CreateFixtureProject(string name)
    {
        var directory = Path.Combine(context.TestTempDirectory!, "project with spaces");
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(project, "<Project />");
        return project;
    }

    private static string FixtureExecutable()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "RefTrace.RuntimeFixture", configuration,
            "RefTrace.RuntimeFixture" + (OperatingSystem.IsWindows() ? ".exe" : "")));
    }

    private static void KillFixtureIfRunning(string pidFile)
    {
        if (!File.Exists(pidFile)) return;
        try
        {
            using var process = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)));
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* The fixture already exited. */ }
        catch (InvalidOperationException) { /* The fixture exited during cleanup. */ }
    }

    [TestMethod]
    public void ParseEvaluation_PreservesValuesAndLabelsTheirPhase()
    {
        var report = AotPublishInspector.ParseEvaluation("""{"Properties":{"TargetFramework":"net10.0","PublishAot":"true","RuntimeIdentifier":"osx-arm64","TargetFrameworks":""}}""", 0);
        Assert.IsTrue(report.Succeeded);
        Assert.AreEqual("MSBuild evaluation before publish", report.Phase);
        Assert.AreEqual("net10.0", report.Properties["TargetFramework"]);
        Assert.AreEqual("true", report.Properties["PublishAot"]);
        Assert.AreEqual("", report.Properties["TargetFrameworks"]);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"Properties\":{\"PublishAot\":true}}")]
    public void ParseEvaluation_UnsupportedOutput_IsNotAValueProof(string output)
    {
        Assert.IsFalse(AotPublishInspector.ParseEvaluation(output, 0).Succeeded);
        var failed = AotPublishInspector.ParseEvaluation("{}", 1, "SDK unavailable");
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("SDK unavailable", failed.Message);
    }
}
