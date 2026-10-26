using System.Text;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class ReleaseCatalogTests
{
    [TestMethod]
    public void ReleaseDecision_DefaultCannotMasqueradeAsAValidChoice()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = default(ReleaseDecision).Version);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = default(ReleaseDecision).Reason);
    }

    [TestMethod]
    public void Read_NumericPrereleaseIdentifiers_UsesNuGetOrdering()
    {
        using var stream = new MemoryStream("""
            {"versions":["2.0.0-rc.9","1.9.0","2.0.0-rc.10"]}
            """u8.ToArray());
        var report = ReleaseCatalog.Read(stream, "index.json", "1.9.0");
        Assert.AreEqual("2.0.0-rc.10", report.Selected);
        Assert.AreEqual("1.9.0", report.LatestStable);
        Assert.AreEqual("newer-prerelease", report.Selection);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    [DataRow("[]", "2.0.0", "current")]
    [DataRow("[\"1.0.0-preview.1\",\"2.0.0\"]", "2.0.0", "current")]
    [DataRow("[\"2.0.0-rc.2\",\"2.0.0\"]", "2.0.0-rc.2", "newer-stable")]
    public void Read_OldPreviewsNeverDowngradeCurrent(string versions, string current, string selection)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"versions\":" + versions + "}"));
        var report = ReleaseCatalog.Read(stream, "index.json", current);
        Assert.AreEqual("2.0.0", report.Selected);
        Assert.AreEqual(selection, report.Selection);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("{\"versions\":[12]}")]
    [DataRow("{\"versions\":[\"invalid\"]}")]
    public void Read_InvalidCatalog_IsRejected(string json)
    {
        Assert.ThrowsExactly<InspectionException>(() =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            ReleaseCatalog.Read(stream, "invalid.json", "1.0.0");
        });
    }
}
