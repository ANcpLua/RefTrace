using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RefTrace.Build;
using RefTrace.Cli;
using RefTrace.Core;
using RefTrace.Metadata;
using RefTrace.Runtime;

namespace RefTrace.Tests;

[TestClass]
public sealed class JsonEnvelopeTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(20)]
    public void PackageEnvelope_IsCompactAndOmitsUnrequestedAssets(int count)
    {
        var report = new PackageReport("/path/quoted \"and\" café.assets.json", "net11.0",
            Enumerable.Range(0, count).Select(index => new PackageItem($"Package.{index}", "1.0.0", index == 0)).ToArray(), [], false);
        var info = RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<PackageReport>>();
        var actual = JsonSerializer.Serialize(new ReportEnvelope<PackageReport>(2, "packages", report), info);

        Assert.IsNotNull(info.SerializeHandler);
        Assert.DoesNotContain("\n", actual);
        using var document = JsonDocument.Parse(actual);
        Assert.AreEqual(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("packages", document.RootElement.GetProperty("kind").GetString());
        var data = document.RootElement.GetProperty("data");
        Assert.AreEqual(count, data.GetProperty("packages").GetArrayLength());
        Assert.AreEqual(report.SourcePath, data.GetProperty("sourcePath").GetString());
        Assert.IsFalse(data.GetProperty("packages")[0].TryGetProperty("compileAssets", out _));
        Assert.IsFalse(data.GetProperty("packages")[0].TryGetProperty("runtimeAssets", out _));
        Assert.IsFalse(data.GetProperty("packages")[0].TryGetProperty("analyzerAssets", out _));
    }

    [TestMethod]
    public void ErrorEnvelope_PreservesDetailsEscapingAndNullOmission()
    {
        var report = new ErrorReport("ambiguous-project", "quote \" newline\nbackslash \\", "/projects", ["a.csproj", "b.csproj"]);
        var info = RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<ErrorReport>>();
        var actual = JsonSerializer.Serialize(new ReportEnvelope<ErrorReport>(2, "error", report), info);

        Assert.IsNotNull(info.SerializeHandler);
        using var document = JsonDocument.Parse(actual);
        var data = document.RootElement.GetProperty("data");
        Assert.AreEqual(report.Message, data.GetProperty("message").GetString());
        Assert.AreEqual("ambiguous-project", data.GetProperty("code").GetString());
        Assert.AreEqual("/projects", data.GetProperty("input").GetString());
        CollectionAssert.AreEqual(new[] { "a.csproj", "b.csproj" }, data.GetProperty("candidates").EnumerateArray().Select(x => x.GetString()).ToArray());
        var absent = JsonSerializer.Serialize(new ReportEnvelope<ErrorReport>(2, "error", new("cancelled", "Cancelled")), info);
        using var absentDocument = JsonDocument.Parse(absent);
        Assert.IsFalse(absentDocument.RootElement.GetProperty("data").TryGetProperty("input", out _));
        Assert.IsFalse(absentDocument.RootElement.GetProperty("data").TryGetProperty("candidates", out _));
    }

    [TestMethod]
    public void Context_RegistersEveryClosedReportEnvelope()
    {
        Type[] envelopeTypes =
        [
            typeof(ReportEnvelope<ErrorReport>), typeof(ReportEnvelope<DependencyGraph>),
            typeof(ReportEnvelope<WhyReport>), typeof(ReportEnvelope<PolicyReport>),
            typeof(ReportEnvelope<AssemblyReport>), typeof(ReportEnvelope<ProcessReport>),
            typeof(ReportEnvelope<RuntimeReport>), typeof(ReportEnvelope<SdkReport>),
            typeof(ReportEnvelope<PackageReport>), typeof(ReportEnvelope<ReflectionReport>),
            typeof(ReportEnvelope<AotInspectionReport>), typeof(ReportEnvelope<AotPublishReport>),
            typeof(ReportEnvelope<PackageReleaseReport>), typeof(ReportEnvelope<ReviewReport>),
            typeof(ReportEnvelope<ToolUpdateReport>)
        ];
        foreach (var envelopeType in envelopeTypes)
        {
            var info = RefTraceJsonContext.Default.GetTypeInfo(envelopeType);
            Assert.IsNotNull(info, envelopeType.FullName);
            Assert.AreEqual(envelopeType, info.Type);
            Assert.AreEqual(JsonTypeInfoKind.Object, info.Kind);
        }
    }
}
