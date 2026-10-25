namespace RefTrace.Metadata;

public sealed record ReflectionFinding(string Caller, string CallerToken, int IlOffset, string Instruction,
    string TargetAssembly, string TargetType, string TargetMember, string Category, string Reason);
public sealed record ReflectionReport(string SourcePath, string AssemblyName, string ModuleVersionId,
    int MethodBodiesScanned, string CatalogVersion, IReadOnlyList<ReflectionFinding> Findings,
    IReadOnlyList<string> Limitations);
public sealed record CompatibilityAnnotation(string Owner, string Attribute, IReadOnlyList<string> Arguments, string ArgumentDecoding);
public sealed record AotInspectionReport(string SourcePath, string Assessment, string ManagedCodeFormat,
    bool? DeclaredAotCompatible, bool? DeclaredTrimmable,
    IReadOnlyList<CompatibilityAnnotation> Annotations, ReflectionReport Reflection,
    IReadOnlyList<string> Limitations);
