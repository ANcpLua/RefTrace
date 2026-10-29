using System.Text.Json.Serialization;
using RefTrace.Core;
using RefTrace.Runtime;
using RefTrace.Metadata;
using RefTrace.Build;

namespace RefTrace.Cli;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReportEnvelope<ErrorReport>))]
[JsonSerializable(typeof(ReportEnvelope<DependencyGraph>))]
[JsonSerializable(typeof(ReportEnvelope<WhyReport>))]
[JsonSerializable(typeof(ReportEnvelope<PolicyReport>))]
[JsonSerializable(typeof(ReportEnvelope<AssemblyReport>))]
[JsonSerializable(typeof(ReportEnvelope<ProcessReport>))]
[JsonSerializable(typeof(ReportEnvelope<RuntimeReport>))]
[JsonSerializable(typeof(ReportEnvelope<SdkReport>))]
[JsonSerializable(typeof(ReportEnvelope<PackageReport>))]
[JsonSerializable(typeof(ReportEnvelope<ReflectionReport>))]
[JsonSerializable(typeof(ReportEnvelope<AotInspectionReport>))]
[JsonSerializable(typeof(ReportEnvelope<AotPublishReport>))]
[JsonSerializable(typeof(ReportEnvelope<PackageReleaseReport>))]
[JsonSerializable(typeof(ReportEnvelope<ReviewReport>))]
[JsonSerializable(typeof(ReviewInputs))]
internal partial class RefTraceJsonContext : JsonSerializerContext;
