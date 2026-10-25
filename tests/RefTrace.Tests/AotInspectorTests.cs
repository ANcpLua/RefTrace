using Microsoft.VisualStudio.TestTools.UnitTesting;
using RefTrace.Metadata;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class AotInspectorTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Read_DeclaredCompatibility_IsNotAProof(bool declared)
    {
        using var image = SafetyPe.Create([0x2a], declared);
        var report = AotInspector.Read(image, "claim.dll");
        Assert.AreEqual(declared, report.DeclaredAotCompatible);
        Assert.AreEqual("not-established", report.Assessment);
        Assert.AreEqual("managed-il", report.ManagedCodeFormat);
        Assert.IsEmpty(report.Reflection.Findings);
    }

    [TestMethod]
    public void Read_RequiresAnnotations_PreservesContractAndMessage()
    {
        var report = AotInspector.Read(typeof(ReflectionSamples).Assembly.Location);
        var dynamic = report.Annotations.Single(a => a.Owner.EndsWith("ReflectionSamples.DynamicContract", StringComparison.Ordinal));
        Assert.AreEqual("System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute", dynamic.Attribute);
        Assert.AreEqual("A fixture requiring runtime code generation.", dynamic.Arguments.Single());
        Assert.IsTrue(report.Annotations.Any(a => a.Owner.EndsWith("ReflectionSamples.TrimmingContract", StringComparison.Ordinal)
            && a.Attribute.EndsWith("RequiresUnreferencedCodeAttribute", StringComparison.Ordinal)));
        Assert.IsNull(report.DeclaredAotCompatible);
    }

    [TestMethod]
    public void Read_NativeOrInvalidData_DoesNotImplyCompatibility()
    {
        using var image = new MemoryStream(new byte[32]);
        Assert.ThrowsExactly<InspectionException>(() => AotInspector.Read(image, "native.bin"));
    }

    [TestMethod]
    public void CompatibilityFacade_ForwardsExistingTypeNames()
    {
        var type = Type.GetType("RefTrace.Core.AssemblyInspector, RefTrace.Core", throwOnError: true);
        Assert.AreSame(typeof(AssemblyInspector), type);
        Assert.AreEqual("RefTrace.Metadata", type!.Assembly.GetName().Name);
        var facade = AssemblyInspector.Read(System.Reflection.Assembly.Load("RefTrace.Core").Location);
        Assert.Contains("RefTrace.Core.AssemblyInspector", facade.ForwardedTypes);
    }
}
