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

public sealed record PublishAgeSetting(string Source, string? SourceName, int MinimumHours,
    IReadOnlyList<string> ExceptionPatterns, IReadOnlyList<string> ConfigFiles);
public sealed record PublishAgeCheck(string Result, string? Published, double? AgeHours, int MinimumHours, string? ExceptionPattern);

public sealed record AssemblyIdentity(string Name, string Version, string? Culture, string? PublicKeyToken);
public sealed record ApiParameter(string Name, string Type, string Passing, bool Optional, string? DefaultValue);
public sealed record ApiGenericParameter(string Name, IReadOnlyList<string> Constraints);
public sealed record ApiMember(string Name, string Kind, string Accessibility, bool IsStatic,
    string Type, IReadOnlyList<ApiParameter> Parameters, IReadOnlyList<ApiGenericParameter> GenericParameters,
    IReadOnlyList<string> Accessors, string Signature)
{
    // The receiver of a C# 14 extension member and the type parameters of its extension block.
    public ApiParameter? Receiver { get; init; }
    public IReadOnlyList<ApiGenericParameter>? ExtensionGenericParameters { get; init; }
    // [RequiresUnsafe]: true; false when the module uses memory-safety rules 2 and the member is unmarked;
    // null when the module records no memory-safety rules, so the absence of the marker says nothing.
    public bool? RequiresUnsafe { get; init; }
}
public sealed record ApiType(string Name, string Kind, IReadOnlyList<ApiGenericParameter> GenericParameters,
    string? BaseType, IReadOnlyList<string> Interfaces, IReadOnlyList<ApiMember> Members)
{
    public IReadOnlyList<string> Modifiers { get; init; } = [];
    // Direct subtypes recorded by IsClosedTypeAttribute; null unless the type is closed.
    public IReadOnlyList<string>? ClosedDerivedTypes { get; init; }
    // Case types of a [Union] type; null unless the type is a union.
    public IReadOnlyList<string>? UnionCases { get; init; }
    public string? Layout { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
public sealed record AssemblyReport(
    string SourcePath, AssemblyIdentity Identity, string ModuleVersionId,
    string? TargetFramework, string? FileVersion, string? InformationalVersion,
    bool IsReferenceAssembly, IReadOnlyList<AssemblyIdentity> References,
    IReadOnlyList<ApiType> Types)
{
    public IReadOnlyList<string> ForwardedTypes { get; init; } = [];
    public IReadOnlyDictionary<string, string?> AssemblyMetadata { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyList<string> DebugDirectoryKinds { get; init; } = [];
    public int? MemorySafetyRulesVersion { get; init; }
}

public sealed class InspectionException(string code, string message, string? input = null,
    IReadOnlyList<string>? candidates = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Input { get; } = input;
    public IReadOnlyList<string>? Candidates { get; } = candidates;
}
