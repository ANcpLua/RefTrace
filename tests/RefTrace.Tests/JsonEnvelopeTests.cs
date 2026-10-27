using System.Text.Json;
using System.Text.Json.Serialization;
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
    public void PackageEnvelope_PreservesLegacyJsonAndHasGeneratedWriter(int count)
    {
        var report = new PackageReport("/path/quoted \"and\" café.assets.json", "net11.0",
            Enumerable.Range(0, count).Select(index => new PackageItem($"Package.{index}", "1.0.0", index == 0,
                ["lib/net11.0/package.dll"], [])).ToArray(), []);
        var expected = JsonSerializer.Serialize(new PreviousEnvelope(1, "packages", report),
            PreviousJsonContext.Default.PreviousEnvelope);
        var info = RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<PackageReport>>();

        var actual = JsonSerializer.Serialize(new ReportEnvelope<PackageReport>(1, "packages", report), info);

        Assert.AreEqual(expected, actual);
        Assert.IsNotNull(info.SerializeHandler);
        using var document = JsonDocument.Parse(actual);
        Assert.AreEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("packages", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual(count, document.RootElement.GetProperty("data").GetProperty("packages").GetArrayLength());
        Assert.AreEqual(report.SourcePath, document.RootElement.GetProperty("data").GetProperty("sourcePath").GetString());
    }

    [TestMethod]
    public void ErrorEnvelope_PreservesLegacyEscapingAndNullOmission()
    {
        var report = new ErrorReport("invalid-input", "quote \" newline\nbackslash \\");
        var info = RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<ErrorReport>>();
        var expected = JsonSerializer.Serialize(new PreviousEnvelope(1, "error", report),
            PreviousJsonContext.Default.PreviousEnvelope);

        var actual = JsonSerializer.Serialize(new ReportEnvelope<ErrorReport>(1, "error", report), info);

        Assert.AreEqual(expected, actual);
        Assert.IsNotNull(info.SerializeHandler);
        using var document = JsonDocument.Parse(actual);
        Assert.AreEqual(report.Message, document.RootElement.GetProperty("data").GetProperty("message").GetString());

        var absent = JsonSerializer.Serialize(new ReportEnvelope<ErrorReport?>(1, "error", null),
            RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<ErrorReport?>>());
        using var absentDocument = JsonDocument.Parse(absent);
        Assert.IsFalse(absentDocument.RootElement.TryGetProperty("data", out _));
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
            typeof(ReportEnvelope<PackageReleaseReport>)
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

internal sealed record PreviousEnvelope(int SchemaVersion, string Kind, object Data);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreviousEnvelope))]
[JsonSerializable(typeof(PackageReport))]
[JsonSerializable(typeof(ErrorReport))]
internal partial class PreviousJsonContext : JsonSerializerContext;
