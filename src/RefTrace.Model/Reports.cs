namespace RefTrace.Core;

public sealed record DependencyNode(
    string Id, string Version, string Kind, string? ProjectPath,
    IReadOnlyList<string> CompileAssets, IReadOnlyList<string> RuntimeAssets);

public sealed record DependencyEdge(string From, string To, string RequestedVersion);

public sealed record DependencyGraph(
    string SourcePath, string ProjectPath, string Target, string Framework,
    string? RuntimeIdentifier, string RootId,
    IReadOnlyList<DependencyNode> Nodes, IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<string> UnresolvedDeclarations);

public sealed record DependencyTrace(string Dependency, bool Found, IReadOnlyList<DependencyNode> Path);
public sealed record PolicyViolation(string Pattern, DependencyNode Dependency, IReadOnlyList<DependencyNode> Path);
public sealed record PolicyReport(string SourcePath, string Target, bool Passed,
    IReadOnlyList<PolicyViolation> Violations, IReadOnlyList<string> UnresolvedDependencies);

public sealed record AssemblyIdentity(string Name, string Version, string? Culture, string? PublicKeyToken);
public sealed record ApiMember(string Name, string Kind, bool IsStatic);
public sealed record ApiType(string Name, IReadOnlyList<ApiMember> Members);
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

public sealed class InspectionException(string message) : Exception(message);
