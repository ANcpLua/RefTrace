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
        foreach (var name in new[] { "ARTIFACTS", "OBJ", "BiN", ".GIT" })
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
    public void FindProjects_SolutionSelectsListedDotNetProjectsRelativeToSolution()
    {
        var source = Directory.CreateDirectory(Path.Combine(Root, "src"));
        var first = Path.Combine(source.FullName, "First.csproj");
        var second = Path.Combine(source.FullName, "Second.fsproj");
        var third = Path.Combine(source.FullName, "Third.VBPROJ");
        foreach (var path in new[] { first, second, third, Path.Combine(Root, "Unlisted.csproj") })
            File.WriteAllText(path, "<Project/>");
        var solutionDirectory = Directory.CreateDirectory(Path.Combine(Root, "solutions"));
        var solution = Path.Combine(solutionDirectory.FullName, "Example.SLNX");
        File.WriteAllText(solution, """
            <Solution>
              <Project Path="../src/Second.fsproj" />
              <Folder Name="/Logical/Not/A/Directory/">
                <Project Path="..\src\First.csproj" />
                <Project Path="../src/./First.csproj" />
                <Project Path="../src/Third.VBPROJ" />
                <Project Path="../missing.esproj" />
                <Project Path="../missing.vcxproj" />
                <File Path="../Unlisted.csproj" />
              </Folder>
            </Solution>
            """);

        var projects = ProjectInputs.FindProjects(solution);

        CollectionAssert.AreEqual(new[] { first, second, third }, projects.ToArray());
    }

    [TestMethod]
    [DataRow("<Properties Name=\"Extension\"><Project Path=\"Unlisted.csproj\" /></Properties>")]
    [DataRow("<Folder Name=\"/Logical/\"><Extension><Project Path=\"Unlisted.csproj\" /></Extension></Folder>")]
    [DataRow("<Folder Name=\"/Logical/\"><Folder Name=\"/Nested/\"><Project Path=\"Unlisted.csproj\" /></Folder></Folder>")]
    [DataRow("<Project Path=\"Selected.csproj\"><Project Path=\"Unlisted.csproj\" /></Project>")]
    [DataRow("<ext:Folder xmlns:ext=\"urn:extension\"><Project Path=\"Unlisted.csproj\" /></ext:Folder>")]
    public void FindProjects_SolutionIgnoresProjectElementsOutsideSupportedLocations(string extension)
    {
        var selected = Path.Combine(Root, "Selected.csproj");
        File.WriteAllText(selected, "<Project/>");
        var solution = Path.Combine(Root, "Scoped.slnx");
        File.WriteAllText(solution, "<Solution><Project Path=\"Selected.csproj\" />" + extension + "</Solution>");

        CollectionAssert.AreEqual(new[] { selected }, ProjectInputs.FindProjects(solution).ToArray());
    }

    [TestMethod]
    [DataRow("<Solution><Project></Solution>", "Malformed XML", DisplayName = "Malformed solution XML")]
    [DataRow("<Project />", "Expected a <Solution> root element", DisplayName = "Wrong solution root")]
    [DataRow("<Solution xmlns=\"urn:other\" />", "Expected a <Solution> root element", DisplayName = "Unsupported solution namespace")]
    [DataRow("<Solution><Project /></Solution>", "non-empty Path attribute", DisplayName = "Missing project path")]
    [DataRow("<Solution><Project Path=\"  \" /></Solution>", "non-empty Path attribute", DisplayName = "Blank project path")]
    [DataRow("<!DOCTYPE Solution [<!ENTITY path 'Example.csproj'>]><Solution><Project Path=\"&path;\" /></Solution>", "Malformed XML", DisplayName = "Solution DTD is rejected")]
    public void FindProjects_InvalidSolutionReportsActionableError(string contents, string message)
    {
        var solution = Path.Combine(Root, "Invalid.slnx");
        File.WriteAllText(solution, contents);

        var error = Assert.ThrowsExactly<InspectionException>(() => ProjectInputs.FindProjects(solution));

        Assert.AreEqual("invalid-solution", error.Code);
        Assert.AreEqual(solution, error.Input);
        Assert.Contains(message, error.Message);
    }

    [TestMethod]
    public void FindProjects_SolutionMissingProjectIdentifiesPathAndSolution()
    {
        var solution = Path.Combine(Root, "Missing.slnx");
        var missing = Path.Combine(Root, "src", "Missing.csproj");
        File.WriteAllText(solution, "<Solution><Project Path=\"src/Missing.csproj\" /></Solution>");

        var error = Assert.ThrowsExactly<InspectionException>(() => ProjectInputs.FindProjects(solution));

        Assert.AreEqual("file-not-found", error.Code);
        Assert.AreEqual(missing, error.Input);
        Assert.Contains(solution, error.Message);
        Assert.Contains("src/Missing.csproj", error.Message);
        Assert.Contains("Restore the file or update its Path", error.Message);
    }

    [TestMethod]
    public async Task Resolve_SingleProjectSolutionUsesEvaluatedAssetsAndIgnoresUnlistedProjects()
    {
        var (project, assets) = Project();
        await File.WriteAllTextAsync(Path.Combine(Root, "Unlisted.csproj"), "not XML", context.CancellationToken);
        var solution = Path.Combine(Root, "Example.slnx");
        await File.WriteAllTextAsync(solution, "<Solution><Folder Name=\"/Logical/\"><Project Path=\"Example.csproj\" /></Folder></Solution>", context.CancellationToken);

        var result = await ProjectInputs.ResolveAsync(solution, context.CancellationToken);

        Assert.AreEqual(project, result.ProjectPath);
        Assert.AreEqual(assets, result.AssetsPath);
        Assert.IsFalse(Directory.Exists(Path.Combine(Root, "obj")));
    }

    [TestMethod]
    public async Task Resolve_MultipleProjectSolutionReportsOnlyListedCandidatesWithoutEvaluation()
    {
        var first = Path.Combine(Root, "First.csproj");
        var second = Path.Combine(Root, "Second.csproj");
        foreach (var path in new[] { first, second, Path.Combine(Root, "Unlisted.csproj") })
            await File.WriteAllTextAsync(path, "not XML", context.CancellationToken);
        var solution = Path.Combine(Root, "Multiple.slnx");
        await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"Second.csproj\" /><Project Path=\"First.csproj\" /></Solution>", context.CancellationToken);

        var error = await Assert.ThrowsExactlyAsync<InspectionException>(() => ProjectInputs.ResolveAsync(solution, context.CancellationToken));

        Assert.AreEqual("ambiguous-project", error.Code);
        Assert.AreEqual(solution, error.Input);
        CollectionAssert.AreEqual(new[] { first, second }, error.Candidates!.ToArray());
        Assert.Contains("ref review", error.Message);
    }

    [TestMethod]
    [DataRow("<Solution />", DisplayName = "Empty solution")]
    [DataRow("<Solution><Project Path=\"missing.esproj\" /><File Path=\"Example.csproj\" /></Solution>", DisplayName = "Solution without dotnet projects")]
    public async Task Resolve_SolutionWithoutDotNetProjectsReportsProjectNotFound(string contents)
    {
        var solution = Path.Combine(Root, "Empty.slnx");
        await File.WriteAllTextAsync(solution, contents, context.CancellationToken);

        var error = await Assert.ThrowsExactlyAsync<InspectionException>(() => ProjectInputs.ResolveAsync(solution, context.CancellationToken));

        Assert.AreEqual("project-not-found", error.Code);
        Assert.AreEqual(solution, error.Input);
        Assert.AreEqual("No .NET project was found in this solution.", error.Message);
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
