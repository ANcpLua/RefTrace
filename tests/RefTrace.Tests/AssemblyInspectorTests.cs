using RefTrace.Core;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace RefTrace.Tests;

[TestClass]
public sealed class AssemblyInspectorTests
{
    [TestMethod]
    public void Read_Assembly_SeparatesIdentityFrameworkAndInformationalVersions()
    {
        var report = AssemblyInspector.Read(typeof(AssemblyInspector).Assembly.Location);
        Assert.AreEqual("RefTrace.Metadata", report.Identity.Name);
        Assert.AreEqual("0.8.0.0", report.Identity.Version);
        Assert.AreEqual(".NETCoreApp,Version=v11.0", report.TargetFramework);
        Assert.AreEqual("0.8.0.0", report.FileVersion);
        Assert.StartsWith("0.8.0", report.InformationalVersion!);
        Assert.IsNull(report.Identity.PublicKeyToken);
        Assert.IsFalse(report.IsReferenceAssembly);
        Assert.IsTrue(Guid.TryParse(report.ModuleVersionId, out _));
        Assert.Contains("RefTrace.Model", report.References.Select(r => r.Name));
        Assert.IsFalse(report.References.Any(r => r.Name == "NuGet.ProjectModel"));
    }

    [TestMethod]
    public void Read_PublicApi_IsReadWithoutRunningTypeInitializerOrGetter()
    {
        var report = AssemblyInspector.Read(typeof(ApiFixture).Assembly.Location, "RefTrace.Tests.ApiFixture*");
        var type = report.Types.Single(t => t.Name == "RefTrace.Tests.ApiFixture");
        var property = type.Members.Single(m => m.Name == nameof(ApiFixture.Dangerous));
        Assert.AreEqual("property", property.Kind);
        Assert.IsTrue(property.IsStatic);
        Assert.AreEqual("System.String", property.Type);
        Assert.IsFalse(type.Members.Any(m => m.Name == nameof(ApiFixture.Hidden)));
        Assert.Contains($"{typeof(ApiFixture).FullName}+{nameof(ApiFixture.Nested)}", report.Types.Select(t => t.Name));
        Assert.IsFalse(report.Types.Any(t => t.Name.Contains("PrivateNested", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Read_StrongNamedAssembly_ReportsItsIdentityToken()
    {
        var report = AssemblyInspector.Read(typeof(object).Assembly.Location);
        Assert.AreEqual("System.Private.CoreLib", report.Identity.Name);
        Assert.AreEqual("7cec85d7bea7798e", report.Identity.PublicKeyToken);
    }

    [TestMethod]
    public void Read_LeavesCallerStreamOpen()
    {
        using var stream = File.OpenRead(typeof(AssemblyInspector).Assembly.Location);
        AssemblyInspector.Read(stream, "fixture.dll");
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public void Read_AssemblyMetadataAndCodeView_AreIndependentEvidence()
    {
        using var image = SafetyPe.Create([0x2a], declaredAot: true, debug: true);
        var report = AssemblyInspector.Read(image, "metadata.dll");
        Assert.AreEqual("True", report.AssemblyMetadata["IsAotCompatible"]);
        Assert.Contains("CodeView", report.DebugDirectoryKinds);
    }

    [TestMethod]
    public void Read_AttributeScopeAndNamespace_AvoidFalseAssemblyClaims()
    {
        using var onType = SafetyPe.Create([0x2a], declaredAot: true, claimOnType: true);
        Assert.IsEmpty(AssemblyInspector.Read(onType, "type.dll").AssemblyMetadata);
        using var unrelated = SafetyPe.Create([0x2a], declaredAot: true, fakeMetadataAttribute: true);
        Assert.IsEmpty(AssemblyInspector.Read(unrelated, "unrelated.dll").AssemblyMetadata);
    }

    [TestMethod]
    public void Read_TypeForwarders_AreReportedWithoutLoadingTarget()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("ForwarderFixture.dll"),
            metadata.GetOrAddGuid(new Guid("60d2c51f-1f49-4e48-9395-7d8e9e5ee2d5")), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("ForwarderFixture"),
            new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var target = metadata.AddAssemblyReference(metadata.GetOrAddString("Absent.Forwarder.Target"),
            new Version(2, 0, 0, 0), default, default, 0, default);
        // ECMA-335 ExportedType.Flags: tdForwarder is not exposed by TypeAttributes.
        metadata.AddExportedType(TypeAttributes.Public | (TypeAttributes)0x00200000,
            metadata.GetOrAddString("Forwarded"), metadata.GetOrAddString("Example"), target, 0);
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        using var stream = new MemoryStream(image.ToArray());

        var report = AssemblyInspector.Read(stream, "forwarder.dll");

        CollectionAssert.AreEqual(new[] { "Forwarded.Example" }, report.ForwardedTypes.ToArray());
        Assert.AreEqual("Absent.Forwarder.Target", report.References.Single().Name);
        Assert.IsEmpty(report.Types);
    }

    [TestMethod]
    public void Read_NonManagedData_IsRejected()
    {
        Assert.ThrowsExactly<InspectionException>(() =>
        {
            using var stream = new MemoryStream(new byte[32]);
            AssemblyInspector.Read(stream, "invalid.dll");
        });
    }

    [TestMethod]
    public void Read_ReferenceAssemblyMarker_IsRecognizedWithoutResolvingAttributes()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("ReferenceFixture.dll"),
            metadata.GetOrAddGuid(new Guid("047a8a14-d825-4f14-9679-93c325115210")), default, default);
        var assembly = metadata.AddAssembly(metadata.GetOrAddString("ReferenceFixture"),
            new Version(1, 2, 3, 4), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0), default, default, 0, default);
        var attribute = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ReferenceAssemblyAttribute"));
        var constructor = metadata.AddMemberReference(attribute, metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
        metadata.AddCustomAttribute(assembly, constructor, metadata.GetOrAddBlob(new byte[] { 1, 0, 0, 0 }));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        using var stream = new MemoryStream(image.ToArray());
        var report = AssemblyInspector.Read(stream, "reference.dll");
        Assert.IsTrue(report.IsReferenceAssembly);
        Assert.AreEqual("1.2.3.4", report.Identity.Version);
        Assert.AreEqual("System.Runtime", report.References.Single().Name);
    }
}

// Members and nested types are metadata fixtures, including deliberately hidden ones.
public sealed class ApiFixture
{
    static ApiFixture() => throw new InvalidOperationException($"Must never execute during metadata inspection: {nameof(PrivateNested)}.");
    public static string Dangerous => throw new InvalidOperationException("Must never invoke getters.");
    internal static string Hidden => "hidden";
    public static class Nested;
    private static class PrivateNested;
}
