using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using RefTrace.Metadata;

namespace RefTrace.Core;

public static class AssemblyInspector
{
    public static AssemblyReport Read(string path, string? typePattern = null)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return Read(stream, fullPath, typePattern);
    }

    public static AssemblyReport Read(Stream stream, string sourcePath, string? typePattern = null)
    {
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) throw new InspectionException("unsupported-artifact", "The file has no managed assembly metadata.");
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) throw new InspectionException("unsupported-artifact", "The file is a module, not an assembly manifest.");
        var definition = reader.GetAssemblyDefinition();
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var isReferenceAssembly = false;
        foreach (var handle in definition.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            var name = AttributeTypeName(reader, attribute.Constructor);
            if (name == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute") isReferenceAssembly = true;
            if (name is not ("System.Runtime.Versioning.TargetFrameworkAttribute"
                or "System.Reflection.AssemblyFileVersionAttribute"
                or "System.Reflection.AssemblyInformationalVersionAttribute")) continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid custom attribute prolog.");
            attributes[name] = blob.ReadSerializedString();
        }

        var references = reader.AssemblyReferences.Select(handle =>
        {
            var reference = reader.GetAssemblyReference(handle);
            return new AssemblyIdentity(reader.GetString(reference.Name), reference.Version.ToString(),
                Text(reader, reference.Culture), Token(reader.GetBlobBytes(reference.PublicKeyOrToken),
                    (reference.Flags & AssemblyFlags.PublicKey) != 0));
        }).OrderBy(r => r.Name, StringComparer.Ordinal).ToArray();

        var types = typePattern is null ? [] : new ApiSurface(reader).Read(typePattern);

        return new AssemblyReport(sourcePath,
            new(reader.GetString(definition.Name), definition.Version.ToString(), Text(reader, definition.Culture),
                Token(reader.GetBlobBytes(definition.PublicKey), isPublicKey: true)),
            reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString(),
            attributes.GetValueOrDefault("System.Runtime.Versioning.TargetFrameworkAttribute"),
            attributes.GetValueOrDefault("System.Reflection.AssemblyFileVersionAttribute"),
            attributes.GetValueOrDefault("System.Reflection.AssemblyInformationalVersionAttribute"),
            isReferenceAssembly, references, types)
        {
            ForwardedTypes = reader.ExportedTypes.Where(h => reader.GetExportedType(h).IsForwarder)
                .Select(h => ForwardedName(reader, h)).Order(StringComparer.Ordinal).ToArray(),
            AssemblyMetadata = PeMetadata.AssemblyProperties(reader),
            DebugDirectoryKinds = pe.ReadDebugDirectory().Select(entry => entry.Type.ToString())
                .Distinct().Order(StringComparer.Ordinal).ToArray()
        };
    }

    private static string ForwardedName(MetadataReader reader, ExportedTypeHandle handle)
    {
        var type = reader.GetExportedType(handle);
        if (type.Implementation.Kind == HandleKind.ExportedType)
            return ForwardedName(reader, (ExportedTypeHandle)type.Implementation) + "+" + reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
    }

    private static string? AttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind == HandleKind.MethodDefinition)
            return TypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
        if (constructor.Kind != HandleKind.MemberReference) return null;
        var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        if (parent.Kind == HandleKind.TypeDefinition) return TypeName(reader, (TypeDefinitionHandle)parent);
        if (parent.Kind != HandleKind.TypeReference) return null;
        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return TypeName(reader, declaring) + "+" + reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
    }

    private static string? Text(MetadataReader reader, StringHandle handle)
        => handle.IsNil ? null : reader.GetString(handle);

    private static string? Token(byte[] bytes, bool isPublicKey)
    {
        if (bytes.Length == 0) return null;
        // ECMA-335 strong-name tokens use the reversed final eight bytes of SHA-1.
        if (isPublicKey) bytes = SHA1.HashData(bytes)[^8..].Reverse().ToArray();
        return Convert.ToHexStringLower(bytes);
    }
}
