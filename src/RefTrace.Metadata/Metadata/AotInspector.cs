using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using RefTrace.Core;

namespace RefTrace.Metadata;

public static class AotInspector
{
    public static AotInspectionReport Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return Read(stream, fullPath);
    }

    public static AotInspectionReport Read(Stream stream, string sourcePath)
    {
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) throw new InspectionException("No managed metadata is available. Native format alone does not establish Native AOT provenance or compatibility.");
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) throw new InspectionException("Expected an assembly manifest.");
        var names = new MetadataNames(reader);
        var properties = PeMetadata.AssemblyProperties(reader);
        bool? aot = bool.TryParse(properties.GetValueOrDefault("IsAotCompatible"), out var aotFlag) ? aotFlag : null;
        bool? trimmable = bool.TryParse(properties.GetValueOrDefault("IsTrimmable"), out var trimFlag) ? trimFlag : null;
        var annotations = new List<CompatibilityAnnotation>();
        foreach (var handle in reader.CustomAttributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            var type = names.Method(attribute.Constructor).Type;
            if (type is not ("System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute"
                or "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"
                or "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute"
                or "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute"
                or "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute")) continue;

            var arguments = new List<string>();
            var decoding = "not-decoded";
            if (type is "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute"
                or "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute")
            {
                var value = reader.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() != 1) throw new BadImageFormatException("Invalid attribute prolog.");
                arguments.Add(value.ReadSerializedString() ?? "");
                decoding = "first-constructor-string";
            }
            annotations.Add(new(names.Owner(attribute.Parent), type, arguments, decoding));
        }

        var format = "managed-il";
        var native = pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory;
        if (native is { Size: >= 4, RelativeVirtualAddress: > 0 })
            format = pe.GetSectionData(native.Value.RelativeVirtualAddress).GetReader(0, 4).ReadUInt32() == 0x00525452
                ? "ready-to-run" : "managed-native-header";

        return new(sourcePath, "not-established", format, aot, trimmable,
            annotations.OrderBy(a => a.Owner, StringComparer.Ordinal).ThenBy(a => a.Attribute, StringComparer.Ordinal).ToArray(),
            ReflectionInspector.Read(pe, sourcePath),
            ["Declared compatibility metadata and static findings are evidence, not a Native AOT validation result.",
             "A successful publish validates only the recorded project, RID, SDK, configuration and reachable code.",
             "ReadyToRun is not Native AOT. Missing IL or metadata is not proof of compatibility.",
             "This scan does not resolve external dependency implementations or perform trimming dataflow analysis."]);
    }
}
