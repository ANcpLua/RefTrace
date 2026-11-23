namespace RefTrace.Metadata;

// Caller, CallerToken and IlOffset locate the IL that contains the call. When that IL is the generated
// MoveNext of an async or iterator state machine, DeclaredMethod names the method the source declared.
public sealed record ReflectionFinding(string Caller, string CallerToken, int IlOffset, string Instruction,
    string TargetAssembly, string TargetType, string TargetMember, string Category, string Reason)
{
    public string? DeclaredMethod { get; init; }
    // Metadata token of the declared method; it tells overloads with the same name apart.
    public string? DeclaredMethodToken { get; init; }
}
public sealed record ReflectionReport(string SourcePath, string AssemblyName, string ModuleVersionId,
    int MethodBodiesScanned, string CatalogVersion, IReadOnlyList<ReflectionFinding> Findings,
    IReadOnlyList<string> Limitations);
public sealed record CompatibilityAnnotation(string Owner, string Attribute, IReadOnlyList<string> Arguments, string ArgumentDecoding);
public sealed record AotInspectionReport(string SourcePath, string Assessment, string ManagedCodeFormat,
    bool? DeclaredAotCompatible, bool? DeclaredTrimmable,
    IReadOnlyList<CompatibilityAnnotation> Annotations, ReflectionReport Reflection,
    IReadOnlyList<string> Limitations);
