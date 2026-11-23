using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace RefTrace.Metadata;

internal sealed class MetadataNames(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    private string Type(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
        _ => handle.Kind.ToString()
    };

    public (string Assembly, string Type, string Member) Method(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodSpecification)
            return Method(reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            return (reader.GetString(reader.GetAssemblyDefinition().Name), Type(method.GetDeclaringType()), reader.GetString(method.Name));
        }
        if (handle.Kind == HandleKind.MemberReference)
        {
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            return (Scope(member.Parent), Type(member.Parent), reader.GetString(member.Name));
        }
        throw new BadImageFormatException("A method instruction has an invalid metadata token.");
    }

    private string Scope(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var scope = reader.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope;
            return scope.Kind switch
            {
                HandleKind.AssemblyReference => reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
                HandleKind.TypeReference => Scope(scope),
                _ => reader.GetString(reader.GetAssemblyDefinition().Name)
            };
        }
        if (handle.Kind == HandleKind.TypeSpecification)
        {
            var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            var code = blob.ReadSignatureTypeCode();
            if (code == SignatureTypeCode.GenericTypeInstance) code = blob.ReadSignatureTypeCode();
            if (code == SignatureTypeCode.TypeHandle) return Scope(blob.ReadTypeHandle());
            return "unknown";
        }
        return reader.GetString(reader.GetAssemblyDefinition().Name);
    }

    public string Owner(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.AssemblyDefinition => reader.GetString(reader.GetAssemblyDefinition().Name),
        HandleKind.TypeDefinition => Type(handle),
        HandleKind.MethodDefinition => Method(handle).Type + "." + Method(handle).Member,
        _ => handle.Kind + "#" + System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle).ToString("x8")
    };

    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = metadata.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return Type(declaring) + "+" + metadata.GetString(type.Name);
        return PeMetadata.Qualified(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
    }
    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = metadata.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference) return Type(type.ResolutionScope) + "+" + metadata.GetString(type.Name);
        return PeMetadata.Qualified(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
    }
    public string GetTypeFromSpecification(MetadataReader metadata, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => metadata.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method-pointer";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
}
