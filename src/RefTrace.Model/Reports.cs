namespace RefTrace.Core;

// AnalyzerAssets is null when the restore did not record analyzer assets (NuGet 8.0, RestoreEnableAnalyzerAssets).
public sealed record DependencyNode(
    string Id, string Version, string Kind, string? ProjectPath,
    IReadOnlyList<string> CompileAssets, IReadOnlyList<string> RuntimeAssets,
    IReadOnlyList<string>? AnalyzerAssets = null);

public sealed record DependencyEdge(string From, string To, string RequestedVersion);

public sealed record DependencyGraph(
    string SourcePath, string ProjectPath, string Target, string Framework,
    string? RuntimeIdentifier, string RootId,
    IReadOnlyList<DependencyNode> Nodes, IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<string> UnresolvedDeclarations)
{
    public bool AnalyzerAssetsRecorded { get; init; }
}

public sealed record DependencyTrace(string Dependency, bool Found, IReadOnlyList<DependencyNode> Path);
public sealed record PolicyViolation(string Pattern, DependencyNode Dependency, IReadOnlyList<DependencyNode> Path);
public sealed record PolicyReport(string SourcePath, string Target, bool Passed,
    IReadOnlyList<PolicyViolation> Violations, IReadOnlyList<string> UnresolvedDependencies);

public sealed record AssemblyIdentity(string Name, string Version, string? Culture, string? PublicKeyToken);
public sealed record ApiParameter(string Name, string Type, string Passing, bool Optional, string? DefaultValue);
public sealed record ApiGenericParameter(string Name, IReadOnlyList<string> Constraints);
public sealed record ApiMember(string Name, string Kind, string Accessibility, bool IsStatic,
    string Type, IReadOnlyList<ApiParameter> Parameters, IReadOnlyList<ApiGenericParameter> GenericParameters,
    IReadOnlyList<string> Accessors, string Signature);
public sealed record ApiType(string Name, string Kind, IReadOnlyList<ApiGenericParameter> GenericParameters,
    string? BaseType, IReadOnlyList<string> Interfaces, IReadOnlyList<ApiMember> Members);
public sealed record AssemblyReport(
    string SourcePath, AssemblyIdentity Identity, string ModuleVersionId,
    string? TargetFramework, string? FileVersion, string? InformationalVersion,
    bool IsReferenceAssembly, IReadOnlyList<AssemblyIdentity> References,
    IReadOnlyList<ApiType> Types)
{
    public IReadOnlyList<string> ForwardedTypes { get; init; } = [];
    public IReadOnlyDictionary<string, string?> AssemblyMetadata { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyList<string> DebugDirectoryKinds { get; init; } = [];
}

public sealed class InspectionException(string code, string message, string? input = null,
    IReadOnlyList<string>? candidates = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Input { get; } = input;
    public IReadOnlyList<string>? Candidates { get; } = candidates;
}
