using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class SdkInspectorTests(TestContext context)
{
    [TestMethod]
    public void Read_ReportsPayloadPresenceWithoutClaimingActivation()
    {
        var path = Path.Combine(context.TestTempDirectory!, "11.0.100-test");
        Directory.CreateDirectory(Path.Combine(path, "Sdks"));
        File.WriteAllText(Path.Combine(path, "dotnet.dll"), "fixture");
        File.WriteAllText(Path.Combine(path, "libdotnet-aot.dylib"), "fixture");
        var report = SdkInspector.Read(path);
        Assert.IsTrue(report.NativeCliPayloadPresent);
        Assert.HasCount(1, report.NativeCliPayloads);
        Assert.AreEqual("11.0.100-test", report.DirectoryVersion);
        Assert.IsNull(report.RoslynAssemblyVersion);
        Assert.IsTrue(report.Notes.Any(n => n.Contains("does not prove", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Read_NonSdkDirectory_IsRejected()
        => Assert.ThrowsExactly<InspectionException>(() => SdkInspector.Read(context.TestTempDirectory!));
}
