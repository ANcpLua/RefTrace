using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using RefTrace.Core;

namespace RefTrace.Metadata;

// Decode signatures from metadata only. No target assembly or attribute is loaded.
internal sealed class ApiSurface(MetadataReader reader) : ISignatureTypeProvider<string, ApiSurface.Context>
{
    internal sealed record Context(string[] TypeParameters, string[] MethodParameters);

    public IReadOnlyList<ApiType> Read(string pattern)
    {
        var result = new List<ApiType>();
        foreach (var handle in reader.TypeDefinitions)
        {
            if (!Visible(handle)) continue;
            var name = GetTypeFromDefinition(reader, handle, 0);
            if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: false)) continue;
            var type = reader.GetTypeDefinition(handle);
            var context = new Context(Names(type.GetGenericParameters()), []);
            var baseType = type.BaseType.IsNil ? null : Type(type.BaseType, context);
            var members = new List<ApiMember>();
            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                var methods = new[] { accessors.Getter, accessors.Setter }.Where(h => !h.IsNil).ToArray();
                accessorMethods.UnionWith(methods);
                var visible = methods.Where(h => Visible(reader.GetMethodDefinition(h).Attributes)).ToArray();
                if (visible.Length == 0) continue;
                var signature = property.DecodeSignature(this, context);
                var getter = !accessors.Getter.IsNil ? reader.GetMethodDefinition(accessors.Getter) : default;
                var parameterOwner = !accessors.Getter.IsNil ? getter : reader.GetMethodDefinition(accessors.Setter);
                var parameters = Parameters(signature.ParameterTypes, parameterOwner.GetParameters());
                var descriptions = new List<string>();
                AddAccessor(descriptions, accessors.Getter, "get", context);
                AddAccessor(descriptions, accessors.Setter, "set", context);
                members.Add(Member(reader.GetString(property.Name), "property", visible.Select(h => reader.GetMethodDefinition(h).Attributes),
                    signature.ReturnType, parameters, [], descriptions));
            }
            foreach (var eventHandle in type.GetEvents())
            {
                var definition = reader.GetEventDefinition(eventHandle);
                var accessors = definition.GetAccessors();
                var methods = new[] { accessors.Adder, accessors.Remover, accessors.Raiser }.Where(h => !h.IsNil).ToArray();
                accessorMethods.UnionWith(methods);
                var visible = methods.Where(h => Visible(reader.GetMethodDefinition(h).Attributes)).ToArray();
                if (visible.Length == 0) continue;
                var descriptions = new List<string>();
                AddAccessor(descriptions, accessors.Adder, "add", context);
                AddAccessor(descriptions, accessors.Remover, "remove", context);
                members.Add(Member(reader.GetString(definition.Name), "event", visible.Select(h => reader.GetMethodDefinition(h).Attributes),
                    Type(definition.Type, context), [], [], descriptions));
            }
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!Visible(method.Attributes) || accessorMethods.Contains(methodHandle)) continue;
                var nameOfMethod = reader.GetString(method.Name);
                var methodContext = context with { MethodParameters = Names(method.GetGenericParameters()) };
                var signature = method.DecodeSignature(this, methodContext);
                members.Add(Member(nameOfMethod, nameOfMethod is ".ctor" or ".cctor" ? "constructor" : "method", [method.Attributes],
                    signature.ReturnType, Parameters(signature.ParameterTypes, method.GetParameters()),
                    Generics(method.GetGenericParameters(), methodContext), []));
            }
            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                var access = field.Attributes & FieldAttributes.FieldAccessMask;
                if (access is not (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem)) continue;
                var accessibility = access switch { FieldAttributes.Public => "public", FieldAttributes.Family => "protected", _ => "protected-internal" };
                var isStatic = (field.Attributes & FieldAttributes.Static) != 0;
                var fieldType = field.DecodeSignature(this, context);
                var fieldName = reader.GetString(field.Name);
                var modifier = (field.Attributes & FieldAttributes.Literal) != 0 ? "const "
                    : (field.Attributes & FieldAttributes.InitOnly) != 0 ? "readonly " : "";
                var constant = Constant(field.GetDefaultValue());
                members.Add(new(fieldName, "field", accessibility, isStatic, fieldType, [], [], [],
                    $"{accessibility} {(isStatic ? "static " : "")}{modifier}{fieldType} {fieldName}" + (constant is null ? "" : " = " + constant)));
            }
            var kind = (type.Attributes & TypeAttributes.Interface) != 0 ? "interface" : baseType switch
            {
                "System.Enum" => "enum", "System.ValueType" => "struct", "System.MulticastDelegate" => "delegate", _ => "class"
            };
            result.Add(new(name, kind, Generics(type.GetGenericParameters(), context), baseType,
                type.GetInterfaceImplementations().Select(h => Type(reader.GetInterfaceImplementation(h).Interface, context)).Order(StringComparer.Ordinal).ToArray(),
                members.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.Signature, StringComparer.Ordinal).ToArray()));
        }
        return result.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
    }

    private ApiMember Member(string name, string kind, IEnumerable<MethodAttributes> attributes, string type,
        IReadOnlyList<ApiParameter> parameters, IReadOnlyList<ApiGenericParameter> generics, IReadOnlyList<string> accessors)
    {
        var flags = attributes.ToArray();
        var accessibility = flags.Any(f => (f & MethodAttributes.MemberAccessMask) == MethodAttributes.Public) ? "public"
            : flags.Any(f => (f & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem) ? "protected-internal" : "protected";
        var isStatic = flags.Any(f => (f & MethodAttributes.Static) != 0);
        var genericText = generics.Count == 0 ? "" : "<" + string.Join(", ", generics.Select(g => g.Name)) + ">";
        var parametersText = string.Join(", ", parameters.Select(p =>
            (p.Passing == "value" ? p.Type : p.Passing + " " + p.Type.TrimEnd('&')) + " " + p.Name
            + (p.DefaultValue is null ? p.Optional ? " [optional]" : "" : " = " + p.DefaultValue)));
        var suffix = kind is "method" or "constructor" ? "(" + parametersText + ")"
            : parameters.Count != 0 ? "[" + parametersText + "]" : "";
        var constraints = string.Concat(generics.Where(g => g.Constraints.Count != 0)
            .Select(g => " where " + g.Name + " : " + string.Join(", ", g.Constraints)));
        var modifiers = flags.Any(f => (f & MethodAttributes.Abstract) != 0) ? "abstract "
            : flags.Any(f => (f & MethodAttributes.Virtual) != 0) ? "virtual " : "";
        var signature = $"{accessibility} {(isStatic ? "static " : "")}{modifiers}{type} {name}{genericText}{suffix}"
            + (accessors.Count == 0 ? "" : " { " + string.Join("; ", accessors) + "; }") + constraints;
        return new(name, kind, accessibility, isStatic, type, parameters, generics, accessors, signature);
    }

    private IReadOnlyList<ApiParameter> Parameters(ImmutableArray<string> types, ParameterHandleCollection handles)
    {
        var metadata = handles.Select(reader.GetParameter).ToDictionary(p => p.SequenceNumber);
        return types.Select((type, index) =>
        {
            var exists = metadata.TryGetValue(index + 1, out var parameter);
            var flags = exists ? parameter.Attributes : 0;
            var passing = !type.EndsWith('&') ? "value" : (flags & ParameterAttributes.Out) != 0 ? "out"
                : (flags & ParameterAttributes.In) != 0 ? "in" : "ref";
            return new ApiParameter(exists && !parameter.Name.IsNil ? reader.GetString(parameter.Name) : "arg" + index,
                type, passing, (flags & ParameterAttributes.Optional) != 0, exists ? Constant(parameter.GetDefaultValue()) : null);
        }).ToArray();
    }

    private string[] Names(GenericParameterHandleCollection handles)
        => handles.Select(h => reader.GetString(reader.GetGenericParameter(h).Name)).ToArray();

    private IReadOnlyList<ApiGenericParameter> Generics(GenericParameterHandleCollection handles, Context context)
        => handles.Select(handle =>
        {
            var parameter = reader.GetGenericParameter(handle);
            var constraints = new List<string>();
            if ((parameter.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0) constraints.Add("class");
            if ((parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) constraints.Add("struct");
            constraints.AddRange(parameter.GetConstraints().Select(h => Type(reader.GetGenericParameterConstraint(h).Type, context)));
            if ((parameter.Attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0) constraints.Add("new()");
            return new ApiGenericParameter(reader.GetString(parameter.Name), constraints);
        }).ToArray();

    private void AddAccessor(List<string> result, MethodDefinitionHandle handle, string name, Context context)
    {
        if (handle.IsNil) return;
        var method = reader.GetMethodDefinition(handle);
        if (!Visible(method.Attributes)) return;
        if (name == "set" && method.DecodeSignature(this, context).ReturnType.Contains("System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal)) name = "init";
        result.Add(name + ":" + ((method.Attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public", MethodAttributes.Family => "protected", _ => "protected-internal"
        }));
    }

    private string? Constant(ConstantHandle handle)
    {
        if (handle.IsNil) return null;
        var constant = reader.GetConstant(handle);
        var value = reader.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode);
        return value switch
        {
            null => "null", string text => "\"" + Escape(text) + "\"", char character => "'" + Escape(character.ToString()) + "'",
            bool boolean => boolean ? "true" : "false", _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private bool Visible(TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        return visibility == TypeAttributes.Public || visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
            && Visible(type.GetDeclaringType());
    }
    private static bool Visible(MethodAttributes attributes) => (attributes & MethodAttributes.MemberAccessMask)
        is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private string Type(EntityHandle handle, Context context) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, context, (TypeSpecificationHandle)handle, 0),
        _ => throw new BadImageFormatException("Invalid type signature handle.")
    };

    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = metadata.GetTypeDefinition(handle);
        return type.GetDeclaringType().IsNil ? Qualified(metadata.GetString(type.Namespace), metadata.GetString(type.Name))
            : GetTypeFromDefinition(metadata, type.GetDeclaringType(), 0) + "+" + metadata.GetString(type.Name);
    }
    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = metadata.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(metadata, (TypeReferenceHandle)type.ResolutionScope, 0) + "+" + metadata.GetString(type.Name)
            : Qualified(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
    }
    private static string Qualified(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    public string GetTypeFromSpecification(MetadataReader metadata, Context context, TypeSpecificationHandle handle, byte rawTypeKind)
        => metadata.GetTypeSpecification(handle).DecodeSignature(this, context);
    public string GetArrayType(string elementType, ArrayShape shape)
        => elementType + "[" + string.Join(",", Enumerable.Range(0, shape.Rank).Select(i =>
            (i < shape.LowerBounds.Length ? shape.LowerBounds[i].ToString(CultureInfo.InvariantCulture) : "") + "..."
            + (i < shape.Sizes.Length ? ((i < shape.LowerBounds.Length ? shape.LowerBounds[i] : 0) + shape.Sizes[i] - 1).ToString(CultureInfo.InvariantCulture) : ""))) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "methodptr[" + signature.Header.CallingConvention + "](" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(Context context, int index) => index < context.MethodParameters.Length ? context.MethodParameters[index] : "!!" + index;
    public string GetGenericTypeParameter(Context context, int index) => index < context.TypeParameters.Length ? context.TypeParameters[index] : "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "System.Void", PrimitiveTypeCode.Boolean => "System.Boolean", PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.SByte => "System.SByte", PrimitiveTypeCode.Byte => "System.Byte", PrimitiveTypeCode.Int16 => "System.Int16", PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32", PrimitiveTypeCode.UInt32 => "System.UInt32", PrimitiveTypeCode.Int64 => "System.Int64", PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single", PrimitiveTypeCode.Double => "System.Double", PrimitiveTypeCode.String => "System.String", PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.IntPtr => "System.IntPtr", PrimitiveTypeCode.UIntPtr => "System.UIntPtr", PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => throw new BadImageFormatException("Unknown primitive signature code.")
    };
    public string GetSZArrayType(string elementType) => elementType + "[]";
}
