using System.Text.Json.Serialization;
using RefTrace.Core;
using RefTrace.Runtime;
using RefTrace.Metadata;
using RefTrace.Build;

namespace RefTrace.Cli;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReportEnvelope))]
[JsonSerializable(typeof(ErrorReport))]
[JsonSerializable(typeof(DependencyGraph))]
[JsonSerializable(typeof(WhyReport))]
[JsonSerializable(typeof(PolicyReport))]
[JsonSerializable(typeof(AssemblyReport))]
[JsonSerializable(typeof(ProcessReport))]
[JsonSerializable(typeof(RuntimeReport))]
[JsonSerializable(typeof(SdkReport))]
[JsonSerializable(typeof(PackageReport))]
[JsonSerializable(typeof(ReflectionReport))]
[JsonSerializable(typeof(AotInspectionReport))]
[JsonSerializable(typeof(AotPublishReport))]
[JsonSerializable(typeof(PackageReleaseReport))]
internal partial class RefTraceJsonContext : JsonSerializerContext;
