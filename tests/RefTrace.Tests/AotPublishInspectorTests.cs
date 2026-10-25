using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        cancellation.Cancel();
        var output = Path.Combine(context.TestTempDirectory!, "not-created");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => AotPublishInspector.RunAsync(
            "unused.csproj", "osx-arm64", output, TimeSpan.FromSeconds(1), cancellationToken: cancellation.Token));
        Assert.IsFalse(Directory.Exists(output));
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
