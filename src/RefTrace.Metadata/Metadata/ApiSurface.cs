using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using RefTrace.Core;

namespace RefTrace.Metadata;

// Decode signatures from metadata only. No target assembly or attribute is loaded.
internal sealed class ApiSurface(MetadataReader reader) : ISignatureTypeProvider<string, ApiSurface.Context>, ICustomAttributeTypeProvider<string>
{
    internal sealed record Context(string[] TypeParameters, string[] MethodParameters);

    private const string RequiresUnsafeAttribute = "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute";
    private readonly MetadataNames names = new(reader);
    private readonly int? memorySafetyRules = PeMetadata.MemorySafetyRulesVersion(reader);

    public IReadOnlyList<ApiType> Read(string pattern)
    {
        var result = new List<ApiType>();
        foreach (var handle in reader.TypeDefinitions)
        {
            if (!Visible(handle) || IsExtensionScaffolding(handle)) continue;
            var name = GetTypeFromDefinition(reader, handle, 0);
            if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: false)) continue;
            var type = reader.GetTypeDefinition(handle);
            var context = new Context(Names(type.GetGenericParameters()), []);
            var baseType = type.BaseType.IsNil ? null : Type(type.BaseType, context);
            var members = Members(type, context, receiverOf: null);
            // C# 14 extension blocks compile to a nested grouping type whose members carry the declarations.
            foreach (var nested in type.GetNestedTypes().Where(IsExtensionGrouping))
                members.AddRange(ExtensionMembers(reader.GetTypeDefinition(nested)));
            var kind = (type.Attributes & TypeAttributes.Interface) != 0 ? "interface" : baseType switch
            {
                "System.Enum" => "enum", "System.ValueType" => "struct", "System.MulticastDelegate" => "delegate", _ => "class"
            };
            var attributes = type.GetCustomAttributes().Select(reader.GetCustomAttribute)
                .ToLookup(attribute => names.Method(attribute.Constructor).Type, StringComparer.Ordinal);
            var closed = attributes["System.Runtime.CompilerServices.IsClosedTypeAttribute"].ToArray();
            var layout = Layout(type, kind, attributes["System.Runtime.InteropServices.ExtendedLayoutAttribute"].ToArray());
            result.Add(new(name, kind, Generics(type.GetGenericParameters(), context), baseType,
                type.GetInterfaceImplementations().Select(h => Type(reader.GetInterfaceImplementation(h).Interface, context)).Order(StringComparer.Ordinal).ToArray(),
                members.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.Signature, StringComparer.Ordinal).ToArray())
            {
                Modifiers = Modifiers(type, kind, closed.Length != 0, attributes.Contains("System.Runtime.CompilerServices.IsReadOnlyAttribute")),
                ClosedDerivedTypes = closed.Length == 0 ? null : DerivedTypes(closed[0]),
                // The compiler treats public single-parameter constructors of a [Union] type as its cases.
                UnionCases = !attributes.Contains("System.Runtime.CompilerServices.UnionAttribute") ? null
                    : members.Where(m => m is { Kind: "constructor", Accessibility: "public", IsStatic: false, Parameters.Count: 1 })
                        .Select(m => m.Parameters[0].Type).Order(StringComparer.Ordinal).ToArray(),
                Layout = layout,
                Notes = layout?.StartsWith("extended", StringComparison.Ordinal) == true ? ExtendedLayoutNotes(type, layout) : []
            });
        }
        return result.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
    }

    private List<ApiMember> Members(TypeDefinition type, Context context, Func<CustomAttributeHandleCollection, ApiParameter?>? receiverOf)
    {
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
                signature.ReturnType, parameters, [], descriptions,
                RequiresUnsafe([property.GetCustomAttributes(), .. visible.Select(h => reader.GetMethodDefinition(h).GetCustomAttributes())]),
                receiverOf?.Invoke(property.GetCustomAttributes())));
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
                Type(definition.Type, context), [], [], descriptions,
                RequiresUnsafe([definition.GetCustomAttributes(), .. visible.Select(h => reader.GetMethodDefinition(h).GetCustomAttributes())]),
                receiverOf?.Invoke(definition.GetCustomAttributes())));
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
                Generics(method.GetGenericParameters(), methodContext), [], RequiresUnsafe([method.GetCustomAttributes()]),
                receiverOf?.Invoke(method.GetCustomAttributes())));
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
            var requiresUnsafe = RequiresUnsafe([field.GetCustomAttributes()]);
            var modifier = (requiresUnsafe == true ? "unsafe " : "") + ((field.Attributes & FieldAttributes.Literal) != 0 ? "const "
                : (field.Attributes & FieldAttributes.InitOnly) != 0 ? "readonly " : "");
            var constant = Constant(field.GetDefaultValue());
            members.Add(new(fieldName, "field", accessibility, isStatic, fieldType, [], [], [],
                $"{accessibility} {(isStatic ? "static " : "")}{modifier}{fieldType} {fieldName}" + (constant is null ? "" : " = " + constant))
            {
                RequiresUnsafe = requiresUnsafe
            });
        }
        return members;
    }

    // Members of an extension grouping type, each with the receiver its marker type declares.
    private IEnumerable<ApiMember> ExtensionMembers(TypeDefinition grouping)
    {
        var context = new Context(Names(grouping.GetGenericParameters()), []);
        var markers = grouping.GetNestedTypes().Select(reader.GetTypeDefinition)
            .ToDictionary(marker => reader.GetString(marker.Name), StringComparer.Ordinal);
        return Members(grouping, context, attributes =>
        {
            foreach (var attribute in attributes.Select(reader.GetCustomAttribute))
            {
                if (names.Method(attribute.Constructor).Type != "System.Runtime.CompilerServices.ExtensionMarkerAttribute") continue;
                if (attribute.DecodeValue(this).FixedArguments is not [{ Value: string markerName }]
                    || !markers.TryGetValue(markerName, out var marker)) break;
                var handle = marker.GetMethods().FirstOrDefault(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "<Extension>$");
                if (handle.IsNil) break;
                var declaration = reader.GetMethodDefinition(handle);
                var markerContext = new Context(Names(marker.GetGenericParameters()), []);
                if (declaration.DecodeSignature(this, markerContext).ParameterTypes is not [var receiverType]) break;
                var receiver = Parameters([receiverType], declaration.GetParameters())[0];
                return declaration.GetParameters().Select(reader.GetParameter).Any(p => p.SequenceNumber == 1 && !p.Name.IsNil)
                    ? receiver : receiver with { Name = "" };
            }
            throw new BadImageFormatException("An extension member has no resolvable extension marker.");
        });
    }

    private static bool IsUnspeakable(TypeDefinition type, MetadataReader metadata)
        => (type.Attributes & TypeAttributes.SpecialName) != 0 && metadata.GetString(type.Name).StartsWith('<');

    private bool IsExtensionGrouping(TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        return IsUnspeakable(type, reader) && type.GetCustomAttributes()
            .Any(h => names.Method(reader.GetCustomAttribute(h).Constructor).Type == "System.Runtime.CompilerServices.ExtensionAttribute");
    }

    // Grouping (<G>$...) and marker (<M>$...) types are compiler scaffolding for extension blocks.
    private bool IsExtensionScaffolding(TypeDefinitionHandle handle)
    {
        for (var current = handle; !current.IsNil; current = reader.GetTypeDefinition(current).GetDeclaringType())
            if (!reader.GetTypeDefinition(current).GetDeclaringType().IsNil && IsUnspeakable(reader.GetTypeDefinition(current), reader)) return true;
        return false;
    }

    // Under memory-safety rules 2 an unmarked member is safe to call; under older rules nothing is recorded.
    private bool? RequiresUnsafe(IEnumerable<CustomAttributeHandleCollection> attributes)
    {
        if (attributes.SelectMany(collection => collection)
            .Any(handle => names.Method(reader.GetCustomAttribute(handle).Constructor).Type == RequiresUnsafeAttribute)) return true;
        return memorySafetyRules >= 2 ? false : null;
    }

    private static IReadOnlyList<string> Modifiers(TypeDefinition type, string kind, bool closed, bool readOnly)
    {
        if (kind == "struct") return readOnly ? ["readonly"] : [];
        if (kind != "class") return [];
        // A closed class is abstract in metadata; C# writes neither abstract nor sealed on it.
        if (closed) return ["closed"];
        var isAbstract = (type.Attributes & TypeAttributes.Abstract) != 0;
        var isSealed = (type.Attributes & TypeAttributes.Sealed) != 0;
        if (isAbstract && isSealed) return ["static"];
        var modifiers = new List<string>();
        if (isAbstract) modifiers.Add("abstract");
        if (isSealed) modifiers.Add("sealed");
        return modifiers;
    }

    private IReadOnlyList<string>? DerivedTypes(CustomAttribute closed)
    {
        foreach (var argument in closed.DecodeValue(this).NamedArguments)
            if (argument is { Name: "DerivedTypes", Value: ImmutableArray<CustomAttributeTypedArgument<string>> types })
                return types.Select(item => (string)item.Value!).Order(StringComparer.Ordinal).ToArray();
        return null;
    }

    private string? Layout(TypeDefinition type, string kind, CustomAttribute[] extended)
    {
        if (kind is not ("class" or "struct")) return null;
        return (type.Attributes & TypeAttributes.LayoutMask) switch
        {
            TypeAttributes.AutoLayout => "auto",
            TypeAttributes.SequentialLayout => "sequential",
            TypeAttributes.ExplicitLayout => "explicit",
            // ECMA-335 adds layout value 0x18 for [ExtendedLayout]; the attribute names the kind.
            _ when extended.Length == 1 && extended[0].DecodeValue(this).FixedArguments is [{ Value: int value }] => value switch
            {
                0 => "extended CStruct", 1 => "extended CUnion", _ => "extended kind " + value.ToString(CultureInfo.InvariantCulture)
            },
            _ => "extended"
        };
    }

    private enum Content { Clean, Reference, Undecided }

    // The runtime refuses to load an extended-layout type that holds a reference, directly or in a nested
    // value type (observed with TypeLoadException on .NET 11.0.0). Metadata alone decides most fields.
    private IReadOnlyList<string> ExtendedLayoutNotes(TypeDefinition type, string layout)
    {
        var notes = new List<string>();
        var context = new Context(Names(type.GetGenericParameters()), []);
        foreach (var field in type.GetFields().Select(reader.GetFieldDefinition).Where(f => (f.Attributes & FieldAttributes.Static) == 0))
        {
            var (content, detail) = FieldContent(field, type, []);
            var description = $"Field '{reader.GetString(field.Name)}' ({field.DecodeSignature(this, context)})";
            if (content == Content.Reference)
                notes.Add($"{description} {detail}. The runtime rejects a type with {layout} layout that holds a reference when it loads the type (TypeLoadException).");
            else if (content == Content.Undecided)
                notes.Add($"{description}: whether it holds a reference cannot be decided from this assembly's metadata ({detail}); {layout} layout fails to load if it does.");
        }
        return notes;
    }

    private (Content Content, string Detail) FieldContent(FieldDefinition field, TypeDefinition owner, HashSet<TypeDefinitionHandle> visiting)
    {
        var blob = reader.GetBlobReader(field.Signature);
        if (blob.ReadSignatureHeader().Kind != SignatureKind.Field) throw new BadImageFormatException("Invalid field signature.");
        return TypeContent(ref blob, owner, visiting);
    }

    private (Content Content, string Detail) TypeContent(ref BlobReader blob, TypeDefinition owner, HashSet<TypeDefinitionHandle> visiting)
    {
        var code = blob.ReadByte();
        while (code is 0x1f or 0x20)
        {
            blob.ReadTypeHandle();
            code = blob.ReadByte();
        }
        switch (code)
        {
            // string, class, array, object, single-dimensional array
            case 0x0e or 0x12 or 0x14 or 0x1c or 0x1d:
                return (Content.Reference, "is a reference type");
            // generic instantiation: class or value type, then the generic type and its arguments
            case 0x15:
                if (blob.ReadByte() == 0x12) return (Content.Reference, "is a reference type");
                return blob.ReadTypeHandle().Kind == HandleKind.TypeDefinition
                    ? (Content.Undecided, "generic value type; depends on its type arguments")
                    : (Content.Undecided, "value type defined in another assembly");
            case 0x11:
                var handle = blob.ReadTypeHandle();
                if (handle.Kind != HandleKind.TypeDefinition)
                    return (Content.Undecided, "value type defined in another assembly");
                var definition = (TypeDefinitionHandle)handle;
                if (!visiting.Add(definition)) return (Content.Clean, "");
                var nested = reader.GetTypeDefinition(definition);
                var result = (Content.Clean, "");
                foreach (var inner in nested.GetFields().Select(reader.GetFieldDefinition).Where(f => (f.Attributes & FieldAttributes.Static) == 0))
                {
                    var (content, _) = FieldContent(inner, nested, visiting);
                    if (content == Content.Reference)
                        return (Content.Reference, $"holds field '{reader.GetString(inner.Name)}' of {GetTypeFromDefinition(reader, definition, 0)}, which is a reference type");
                    if (content == Content.Undecided) result = (Content.Undecided, $"field '{reader.GetString(inner.Name)}' of {GetTypeFromDefinition(reader, definition, 0)} cannot be decided");
                }
                return result;
            case 0x13:
                var index = blob.ReadCompressedInteger();
                var parameters = owner.GetGenericParameters();
                var unmanaged = index < parameters.Count && reader.GetGenericParameter(parameters[index]).GetCustomAttributes()
                    .Any(h => names.Method(reader.GetCustomAttribute(h).Constructor).Type == "System.Runtime.CompilerServices.IsUnmanagedAttribute");
                return unmanaged ? (Content.Clean, "") : (Content.Undecided, "type parameter without an unmanaged constraint");
            default:
                return (Content.Clean, "");
        }
    }

    private ApiMember Member(string name, string kind, IEnumerable<MethodAttributes> attributes, string type,
        IReadOnlyList<ApiParameter> parameters, IReadOnlyList<ApiGenericParameter> generics, IReadOnlyList<string> accessors,
        bool? requiresUnsafe, ApiParameter? receiver)
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
        // A final virtual method (an implicit interface implementation, for example) cannot be overridden.
        var modifiers = flags.Any(f => (f & MethodAttributes.Abstract) != 0) ? "abstract "
            : flags.Any(f => (f & MethodAttributes.Virtual) != 0 && (f & MethodAttributes.Final) == 0) ? "virtual " : "";
        var extension = receiver is null ? "" : "extension(" + (receiver.Passing == "value" ? receiver.Type : receiver.Passing + " " + receiver.Type.TrimEnd('&'))
            + (receiver.Name.Length == 0 ? "" : " " + receiver.Name) + ") ";
        var signature = $"{extension}{accessibility} {(isStatic ? "static " : "")}{(requiresUnsafe == true ? "unsafe " : "")}{modifiers}{type} {name}{genericText}{suffix}"
            + (accessors.Count == 0 ? "" : " { " + string.Join("; ", accessors) + "; }") + constraints;
        return new(name, kind, accessibility, isStatic, type, parameters, generics, accessors, signature)
        {
            RequiresUnsafe = requiresUnsafe,
            Receiver = receiver
        };
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
    public string GetSystemType() => "System.Type";
    public bool IsSystemType(string type) => type == "System.Type";
    public string GetTypeFromSerializedName(string name) => name;
    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => type == "System.Runtime.InteropServices.ExtendedLayoutKind"
        ? PrimitiveTypeCode.Int32 : throw new BadImageFormatException($"Enum {type} is not decoded in custom attributes.");
}
