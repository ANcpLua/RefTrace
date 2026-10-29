using System.Text.Json.Nodes;
using RefTrace.Build;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class ProjectInputTests(TestContext context)
{
    private string Root => context.TestTempDirectory!;

    private (string Project, string Assets) Project(string name = "Example", bool restored = true, string? recorded = null)
    {
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), "<Project/>");
        var project = Path.Combine(Root, name + ".csproj");
        var assets = Path.Combine(Root, "custom restore", name + ".assets.json");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><ProjectAssetsFile>" +
            System.Security.SecurityElement.Escape(assets) + "</ProjectAssetsFile></PropertyGroup></Project>");
        if (restored)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            File.WriteAllText(assets, Fixtures.Modify(root => root["project"] = new JsonObject
            {
                ["restore"] = new JsonObject { ["projectPath"] = recorded ?? project }
            }));
        }
        return (project, assets);
    }

    [TestMethod]
    public async Task Resolve_ProjectUsesEvaluatedCustomAssetsPathWithoutRestore()
    {
        var (project, assets) = Project();
        var before = await File.ReadAllBytesAsync(assets, context.CancellationToken);
        var result = await ProjectInputs.ResolveAsync(project, context.CancellationToken);
        Assert.AreEqual(project, result.ProjectPath);
        Assert.AreEqual(assets, result.AssetsPath);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(assets, context.CancellationToken));
        Assert.IsFalse(Directory.Exists(Path.Combine(Root, "obj")));
        var directoryResult = await ProjectInputs.ResolveAsync(Root, context.CancellationToken);
        Assert.AreEqual(result, directoryResult);
    }

    [TestMethod]
    public async Task Resolve_MissingRestoreAndForeignAssetsHaveDistinctErrors()
    {
        var (project, assets) = Project(restored: false);
        var missing = await Assert.ThrowsExactlyAsync<InspectionException>(() => ProjectInputs.ResolveAsync(project, context.CancellationToken));
        Assert.AreEqual("restore-required", missing.Code);
        Assert.AreEqual(assets, missing.Input);
        Project(recorded: Path.Combine(Root, "Other.csproj"));
        var foreign = await Assert.ThrowsExactlyAsync<InspectionException>(() => ProjectInputs.ResolveAsync(project, context.CancellationToken));
        Assert.AreEqual("mismatched-assets", foreign.Code);
    }

    [TestMethod]
    public async Task Resolve_DirectoryAmbiguityListsProjectsAndIgnoresBuildArtifacts()
    {
        var first = Project("A").Project;
        var second = Project("B").Project;
        foreach (var name in new[] { "artifacts", "obj", "bin", ".git" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(Root, name));
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "Ignored.csproj"), "<Project/>", context.CancellationToken);
        }
        var error = await Assert.ThrowsExactlyAsync<InspectionException>(() => ProjectInputs.ResolveAsync(Root, context.CancellationToken));
        Assert.AreEqual("ambiguous-project", error.Code);
        CollectionAssert.AreEqual(new[] { first, second }, error.Candidates!.ToArray());
    }

    [TestMethod]
    public async Task Resolve_ExplicitAssetsNeedsNoProjectEvaluation()
    {
        var path = Path.Combine(Root, "explicit.json");
        await File.WriteAllTextAsync(path, Fixtures.Assets, context.CancellationToken);
        var result = await ProjectInputs.ResolveAsync(path, context.CancellationToken);
        Assert.IsNull(result.ProjectPath);
        Assert.AreEqual(path, result.AssetsPath);
    }

    [TestMethod]
    public async Task ResolveSdk_UsesSelectedSdkAndAcceptsExplicitSdkDirectory()
    {
        var result = await ProjectInputs.ResolveSdkAsync(Root, context.CancellationToken);
        Assert.IsTrue(File.Exists(Path.Combine(result, "dotnet.dll")));
        Assert.IsTrue(Directory.Exists(Path.Combine(result, "Sdks")));
        Assert.AreEqual(result, await ProjectInputs.ResolveSdkAsync(result, context.CancellationToken));
    }
}
