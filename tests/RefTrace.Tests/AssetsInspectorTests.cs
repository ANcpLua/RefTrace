using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class AssetsInspectorTests
{
    [TestMethod]
    public void Read_PreservesResolvedVersionsAssetsAndProjectReference()
    {
        var graph = Fixtures.Read();
        Assert.AreEqual("net10.0", graph.Framework);
        Assert.AreEqual("/fixture/project.assets.json", graph.SourcePath);
        Assert.HasCount(4, graph.Nodes);
        Assert.HasCount(3, graph.Edges);
        var project = graph.Nodes.Single(n => n.Id == "App.Core");
        Assert.AreEqual("project", project.Kind);
        Assert.AreEqual("../App.Core/App.Core.csproj", project.ProjectPath);
        var alpha = graph.Nodes.Single(n => n.Id == "Alpha");
        Assert.AreEqual("2.0.0", alpha.Version);
        CollectionAssert.AreEqual(new[] { "ref/net10.0/Alpha.dll" }, alpha.CompileAssets.ToArray());
        CollectionAssert.AreEqual(new[] { "lib/net10.0/Alpha.dll" }, alpha.RuntimeAssets.ToArray());
        Assert.IsEmpty(graph.UnresolvedDeclarations);
    }

    [TestMethod]
    public void Read_MultipleTargets_RequiresExplicitSelection()
    {
        var json = Fixtures.Modify(j => j["targets"]!["net10.0/linux-x64"] = j["targets"]!["net10.0"]!.DeepClone());
        Assert.ThrowsExactly<InspectionException>(() => Fixtures.Read(json));
        var graph = Fixtures.Read(json, "net10.0/linux-x64");
        Assert.AreEqual("linux-x64", graph.RuntimeIdentifier);
        Assert.AreEqual("net10.0", graph.Framework);
    }

    [TestMethod]
    public void Read_VersionFourAndAlias_SelectsMatchingDependencyGroup()
    {
        var json = Fixtures.Modify(j =>
        {
            j["version"] = 4;
            j["targets"] = new JsonObject { ["modern"] = j["targets"]!["net10.0"]!.DeepClone() };
            j["projectFileDependencyGroups"] = new JsonObject { ["modern"] = new JsonArray("App.Core >= 1.0.0") };
            j["project"] = JsonNode.Parse("""
                { "version": "1.0.0", "restore": { "projectUniqueName": "/app/App.csproj", "projectPath": "/app/App.csproj", "projectName": "App", "originalTargetFrameworks": ["modern"], "frameworks": { "modern": { "targetAlias": "modern" } } }, "frameworks": { "modern": { "framework": "net10.0", "targetAlias": "modern", "dependencies": { "App.Core": { "target": "Project", "version": "[1.0.0, )" } } } } }
                """);
        });
        var graph = Fixtures.Read(json, "modern");
        Assert.AreEqual("net10.0", graph.Framework);
        Assert.AreEqual("/app/App.csproj", graph.ProjectPath);
        Assert.AreEqual("App.Core", graph.Edges.Single(e => e.From == graph.RootId).To);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("{}")]
    [DataRow("{\"version\":99,\"targets\":{\"net10.0\":{}}}")]
    public void Read_InvalidOrUnsupportedArtifacts_AreRejected(string json)
        => Assert.ThrowsExactly<InspectionException>(() => Fixtures.Read(json));

    [TestMethod]
    public void Read_OmittedTransitiveLibrary_IsExplicitlyUnresolved()
    {
        var json = Fixtures.Modify(j => j["targets"]!["net10.0"]!.AsObject().Remove("Beta/3.0.0"));
        var graph = Fixtures.Read(json);
        Assert.AreEqual("unresolved", graph.Nodes.Single(n => n.Id == "Beta").Kind);
        Assert.AreEqual("Alpha", graph.Edges.Single(e => e.To == "Beta").From);
    }

    [TestMethod]
    public void Read_OmittedDirectLibrary_PreservesUncertainty()
    {
        var json = Fixtures.Modify(j => j["projectFileDependencyGroups"]!["net10.0"]!.AsArray().Add("Pruned >= 1.0.0"));
        var graph = Fixtures.Read(json);
        CollectionAssert.AreEqual(new[] { "Pruned >= 1.0.0" }, graph.UnresolvedDeclarations.ToArray());
    }

    [TestMethod]
    public void Read_FailedRestore_IsRejected()
    {
        var json = Fixtures.Modify(j => j["logs"] = JsonNode.Parse("""[{"code":"NU1101","level":"Error","message":"Missing dependency"}]"""));
        Assert.ThrowsExactly<InspectionException>(() => Fixtures.Read(json));
    }

    [TestMethod]
    public void Read_UnknownTarget_IsRejected()
        => Assert.ThrowsExactly<InspectionException>(() => Fixtures.Read(target: "net9.0"));

    [TestMethod]
    public void Read_MissingDirectDependencyGroup_IsRejected()
    {
        var json = Fixtures.Modify(j => j["projectFileDependencyGroups"] = new JsonObject());
        Assert.ThrowsExactly<InspectionException>(() => Fixtures.Read(json));
    }

    [TestMethod]
    public void Read_EmptyResolvedTarget_IsAValidEmptyGraph()
    {
        var json = """{"version":3,"targets":{"net10.0":{}},"libraries":{},"projectFileDependencyGroups":{"net10.0":[]}}""";
        var graph = Fixtures.Read(json);
        Assert.HasCount(1, graph.Nodes);
        Assert.IsEmpty(graph.Edges);
        Assert.IsTrue(GraphQueries.Check(graph, ["Anything"], []).Passed);
    }
}
