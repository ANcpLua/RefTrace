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
        var dynamic = report.Annotations.Single(a => a.Owner.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.DynamicContract)}", StringComparison.Ordinal));
        Assert.AreEqual("System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute", dynamic.Attribute);
        Assert.AreEqual("A fixture requiring runtime code generation.", dynamic.Arguments.Single());
        Assert.IsTrue(report.Annotations.Any(a => a.Owner.EndsWith($"{nameof(ReflectionSamples)}.{nameof(ReflectionSamples.TrimmingContract)}", StringComparison.Ordinal)
            && a.Attribute.EndsWith("RequiresUnreferencedCodeAttribute", StringComparison.Ordinal)));
        Assert.IsNull(report.DeclaredAotCompatible);
    }

    [TestMethod]
    public void Read_NativeOrInvalidData_DoesNotImplyCompatibility()
    {
        Assert.ThrowsExactly<InspectionException>(() =>
        {
            using var image = new MemoryStream(new byte[32]);
            AotInspector.Read(image, "native.bin");
        });
    }
}
