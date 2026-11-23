using System.Security.Cryptography;
using System.Text;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.RuntimeModel;

namespace RefTrace.Tests;

[TestClass]
public sealed class DependencyCompatibilityTests(TestContext context)
{
    [TestMethod]
    public void NuGetLockFile_RoundTripPreservesGraphAndAssets()
    {
        // Parse uses System.Text.Json; Render exercises NuGet's Newtonsoft.Json writer.
        var format = new LockFileFormat();
        var rendered = format.Render(format.Parse(Fixtures.Assets, "/fixture/project.assets.json"));
        var graph = Fixtures.Read(rendered);

        Assert.AreEqual("net10.0", graph.Framework);
        Assert.HasCount(4, graph.Nodes);
        Assert.HasCount(3, graph.Edges);
        var alpha = graph.Nodes.Single(node => node.Id == "Alpha");
        Assert.AreEqual("2.0.0", alpha.Version);
        CollectionAssert.AreEqual(new[] { "ref/net10.0/Alpha.dll" }, alpha.CompileAssets.ToArray());
        CollectionAssert.AreEqual(new[] { "lib/net10.0/Alpha.dll" }, alpha.RuntimeAssets.ToArray());
        var beta = graph.Edges.Single(edge => edge.To == "Beta");
        Assert.AreEqual("Alpha", beta.From);
        Assert.AreEqual("[3.0.0, )", beta.RequestedVersion);
        Assert.AreEqual("App.Core", graph.Edges.Single(edge => edge.From == graph.RootId).To);
    }

    [TestMethod]
    public void NuGetLockFile_RoundTripPreservesAnalyzerAssets()
    {
        var format = new LockFileFormat();
        var rendered = format.Render(format.Parse(Fixtures.WithAnalyzerAssets(), "/fixture/project.assets.json"));
        var graph = Fixtures.Read(rendered);

        Assert.IsTrue(graph.AnalyzerAssetsRecorded);
        Assert.HasCount(2, graph.Nodes.Single(node => node.Id == "Alpha").AnalyzerAssets!);
    }

    [TestMethod]
    public void NuGetRuntimeGraph_RoundTripPreservesImportsConstraintsAndSupport()
    {
        const string input = """
            {
              "runtimes": {
                "test-arm64": {
                  "#import": ["unix-arm64", "any"],
                  "Example.Package": { "Other.Dependency": "[1.2.0, 2.0.0)" }
                },
                "any": {}
              },
              "supports": { "test-context": { "net11.0": ["test-arm64", "any"] } }
            }
            """;
        using var reader = new StringReader(input);
        var path = Path.Combine(context.TestTempDirectory!, "runtime.json");
        JsonRuntimeFormat.WriteRuntimeGraph(path, JsonRuntimeFormat.ReadRuntimeGraph(reader));
        var graph = JsonRuntimeFormat.ReadRuntimeGraph(path);

        var runtime = graph.Runtimes["test-arm64"];
        CollectionAssert.AreEqual(new[] { "unix-arm64", "any" }, runtime.InheritedRuntimes.ToArray());
        var dependency = runtime.RuntimeDependencySets["Example.Package"].Dependencies["Other.Dependency"];
        Assert.AreEqual("[1.2.0, 2.0.0)", dependency.VersionRange.ToNormalizedString());
        var contexts = graph.Supports["test-context"].RestoreContexts;
        CollectionAssert.AreEquivalent(new[] { "test-arm64", "any" }, contexts.Select(item => item.RuntimeIdentifier).ToArray());
        Assert.IsTrue(contexts.All(item => item.Framework.GetShortFolderName() == "net11.0"));
    }

    [TestMethod]
    public void NuGetEncryption_RoundTripsOnWindowsAndRejectsUnsupportedPlatforms()
    {
        const string plaintext = "reftrace-test-only: Grüße 🔒";
        if (!OperatingSystem.IsWindows())
        {
            Assert.ThrowsExactly<NotSupportedException>(() => EncryptionUtility.EncryptString(plaintext));
            Assert.ThrowsExactly<NotSupportedException>(() => EncryptionUtility.DecryptString("synthetic"));
            return;
        }

        var encrypted = EncryptionUtility.EncryptString(plaintext);
        Assert.AreNotEqual(plaintext, encrypted);
        Assert.AreEqual(plaintext, EncryptionUtility.DecryptString(encrypted));

        // Verify both directions across NuGet's actual CurrentUser/entropy contract.
        var entropy = "NuGet"u8.ToArray();
        var decrypted = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), entropy, DataProtectionScope.CurrentUser);
        Assert.AreEqual(plaintext, Encoding.UTF8.GetString(decrypted));
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), entropy, DataProtectionScope.CurrentUser);
        Assert.AreEqual(plaintext, EncryptionUtility.DecryptString(Convert.ToBase64String(protectedBytes)));
    }
}
