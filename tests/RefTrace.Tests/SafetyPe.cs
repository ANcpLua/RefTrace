using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace RefTrace.Tests;

internal static class SafetyPe
{
    public static MemoryStream Create(byte[] il, bool? declaredAot = null, string? reflectionScope = null,
        bool claimOnType = false, bool debug = false, bool fakeMetadataAttribute = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("SafetyFixture.dll"),
            metadata.GetOrAddGuid(new Guid("92753081-5c95-4bb6-9bfd-61bf89cefd5f")), default, default);
        var assembly = metadata.AddAssembly(metadata.GetOrAddString("SafetyFixture"), new Version(1, 0, 0, 0),
            default, default, 0, AssemblyHashAlgorithm.None);
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString(reflectionScope ?? "System.Runtime"), new Version(10, 0, 0, 0),
            default, default, 0, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var objectType = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var definition = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Fixture"), metadata.GetOrAddString("Example"),
            objectType, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        if (reflectionScope is not null)
        {
            var type = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Type"));
            var signature = new BlobBuilder();
            signature.WriteBytes(new byte[] { 0, 1, 0x12 });
            signature.WriteCompressedInteger((MetadataTokens.GetRowNumber(type) << 2) | 1);
            signature.WriteByte(0x0e);
            var member = metadata.AddMemberReference(type, metadata.GetOrAddString("GetType"), metadata.GetOrAddBlob(signature));
            var code = new BlobBuilder();
            code.WriteByte(0x14);
            code.WriteByte(0x28);
            code.WriteInt32(MetadataTokens.GetToken(member));
            code.WriteByte(0x26);
            code.WriteByte(0x2a);
            il = code.ToArray();
        }
        var bodies = new BlobBuilder();
        var instructions = new InstructionEncoder(new BlobBuilder());
        instructions.CodeBuilder.WriteBytes(il);
        var offset = new MethodBodyStreamEncoder(bodies).AddMethodBody(instructions);
        metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"), metadata.GetOrAddBlob(new byte[] { 0, 0, 1 }), offset,
            MetadataTokens.ParameterHandle(1));
        if (declaredAot is not null)
        {
            var type = metadata.AddTypeReference(runtime, metadata.GetOrAddString(fakeMetadataAttribute ? "Unrelated" : "System.Reflection"), metadata.GetOrAddString("AssemblyMetadataAttribute"));
            var constructor = metadata.AddMemberReference(type, metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 2, 1, 0x0e, 0x0e }));
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteSerializedString("IsAotCompatible");
            value.WriteSerializedString(declaredAot.Value.ToString());
            value.WriteUInt16(0);
            metadata.AddCustomAttribute(claimOnType ? (EntityHandle)definition : assembly, constructor, metadata.GetOrAddBlob(value));
        }
        var debugDirectory = new DebugDirectoryBuilder();
        if (debug) debugDirectory.AddCodeViewEntry("fixture.pdb", new BlobContentId(new Guid("065d9de6-2e90-4b14-9c7e-d2a2b5d04f6b"), 1), 0x0100);
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), bodies, debugDirectoryBuilder: debugDirectory, flags: CorFlags.ILOnly);
        var result = new BlobBuilder();
        pe.Serialize(result);
        return new(result.ToArray());
    }
}
