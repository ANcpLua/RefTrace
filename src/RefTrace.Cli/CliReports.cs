using RefTrace.Core;
using RefTrace.Runtime;

namespace RefTrace.Cli;

internal sealed record ReportEnvelope<T>(int SchemaVersion, string Kind, T Data);
internal sealed record ErrorReport(string Code, string Message, string? Input = null, IReadOnlyList<string>? Candidates = null);
internal sealed record WhyReport(string SourcePath, string Target, DependencyTrace Trace);
internal sealed record ProcessReport(IReadOnlyList<DiagnosticProcess> Processes);
internal sealed record PackageItem(string Id, string Version, bool Direct,
    IReadOnlyList<string>? CompileAssets = null, IReadOnlyList<string>? RuntimeAssets = null);
internal sealed record PackageReport(string SourcePath, string Target, IReadOnlyList<PackageItem> Packages,
    IReadOnlyList<string> UnresolvedDependencies);
