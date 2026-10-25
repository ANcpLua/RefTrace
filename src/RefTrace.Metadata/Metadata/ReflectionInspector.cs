using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RefTrace.Core;

namespace RefTrace.Metadata;

public static class ReflectionInspector
{
    public static ReflectionReport Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return Read(stream, fullPath);
    }

    public static ReflectionReport Read(Stream stream, string sourcePath)
    {
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        return Read(pe, sourcePath);
    }

    internal static ReflectionReport Read(PEReader pe, string sourcePath)
    {
        if (!pe.HasMetadata) throw new InspectionException("Reflection inspection requires a managed PE with IL; native code alone is not proof of reflection absence.");
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) throw new InspectionException("Expected an assembly manifest.");
        var names = new MetadataNames(reader);
        var findings = new List<ReflectionFinding>();
        var count = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            count++;
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var caller = names.Method(handle);
            foreach (var call in IlReader.Calls(body.GetILContent().AsSpan()))
            {
                var target = names.Method(MetadataTokens.EntityHandle(call.Token));
                var classification = Classify(target.Assembly, target.Type, target.Member);
                if (classification is null) continue;
                findings.Add(new(caller.Type + "." + caller.Member, "0x" + MetadataTokens.GetToken(handle).ToString("x8"),
                    call.Offset, call.Instruction.ToString(), target.Assembly, target.Type, target.Member,
                    classification.Value.Category, classification.Value.Reason));
            }
        }
        return new(sourcePath, reader.GetString(reader.GetAssemblyDefinition().Name),
            reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString(), count, "1", findings,
            ["Static IL method-reference catalog (calls and delegate targets); presence does not prove execution, reachability or AOT incompatibility.",
             "Absence does not exclude reflection behind dependencies, delegates, native code, or other APIs.",
             "Reference assemblies may contain no implementation IL; source-level dataflow is not analyzed."]);
    }

    private static (string Category, string Reason)? Classify(string assembly, string type, string member)
    {
        if (!(assembly is "mscorlib" or "netstandard" or "System.Private.CoreLib" or "System.Runtime" or "System.Reflection"
            or "System.Reflection.Extensions" or "System.Reflection.Emit" or "System.Reflection.Emit.Lightweight"
            or "System.Linq.Expressions" or "System.Runtime.Loader")) return null;
        if (type == "System.Reflection.Assembly" && member is "Load" or "LoadFrom" or "LoadFile" or "UnsafeLoadFrom")
            return ("assembly-loading", "Loads an assembly dynamically; inspect the source and deployment assumptions.");
        if (type == "System.Runtime.Loader.AssemblyLoadContext" && member is "LoadFromAssemblyPath" or "LoadFromStream" or "LoadFromNativeImage")
            return ("assembly-loading", "Loads an assembly through an AssemblyLoadContext.");
        if (type == "System.Reflection.Assembly" && member is "GetType" or "GetTypes" or "GetExportedTypes")
            return ("reflection-discovery", "Discovers types from an assembly at runtime.");
        if ((type == "System.Reflection.TypeInfo" && member.StartsWith("GetDeclared", StringComparison.Ordinal))
            || (type == "System.Reflection.RuntimeReflectionExtensions" && member.StartsWith("GetRuntime", StringComparison.Ordinal)))
            return ("reflection-discovery", "Discovers runtime members through reflection helper APIs.");
        if (type == "System.Type" && (member is "GetType" or "MakeGenericType" || member.StartsWith("GetMethod", StringComparison.Ordinal)
            || member.StartsWith("GetPropert", StringComparison.Ordinal) || member.StartsWith("GetField", StringComparison.Ordinal)
            || member.StartsWith("GetMember", StringComparison.Ordinal) || member.StartsWith("GetConstructor", StringComparison.Ordinal)
            || member == "InvokeMember"))
            return ("reflection-discovery", "Discovers runtime types or members; preservation depends on dataflow and annotations.");
        if (type == "System.Activator" && member.StartsWith("CreateInstance", StringComparison.Ordinal))
            return ("reflection-activation", "Creates an instance through runtime type information.");
        if (type.StartsWith("System.Reflection.", StringComparison.Ordinal) && member is "Invoke" or "GetValue" or "SetValue" or "MakeGenericMethod" or "CreateDelegate")
            return ("reflection-invocation", "Invokes or accesses reflected members; this is a review point, not a compatibility verdict.");
        if (type.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal))
            return ("dynamic-code", "Uses Reflection.Emit APIs; native-code generation generally needs review for Native AOT.");
        if (type.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal) && member == "Compile")
            return ("expression-compilation", "Compiles an expression; interpreter choices and runtime support require source-level review.");
        return null;
    }
}
