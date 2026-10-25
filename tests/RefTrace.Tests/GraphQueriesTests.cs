using Microsoft.VisualStudio.TestTools.UnitTesting;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class GraphQueriesTests
{
    [TestMethod]
    public void Explain_TransitiveDependency_ReturnsCompleteCaseInsensitivePath()
    {
        var trace = GraphQueries.Explain(Fixtures.Read(), "bEtA");
        Assert.IsTrue(trace.Found);
        CollectionAssert.AreEqual(new[] { "$project", "App.Core", "Alpha", "Beta" }, trace.Path.Select(n => n.Id).ToArray());
    }

    [TestMethod]
    public void Explain_CycleAndShorterRoute_TerminatesWithShortestPath()
    {
        var graph = Fixtures.Read();
        graph = graph with { Edges = graph.Edges.Concat(new[]
        {
            new DependencyEdge("Beta", "Alpha", "2.0.0"),
            new DependencyEdge(graph.RootId, "Beta", "3.0.0")
        }).ToArray() };
        var trace = GraphQueries.Explain(graph, "Beta");
        CollectionAssert.AreEqual(new[] { "$project", "Beta" }, trace.Path.Select(n => n.Id).ToArray());
    }

    [TestMethod]
    public void Explain_AbsentDependency_ReturnsNotFoundAndEmptyPath()
    {
        var trace = GraphQueries.Explain(Fixtures.Read(), "Missing");
        Assert.IsFalse(trace.Found);
        Assert.IsEmpty(trace.Path);
    }

    [TestMethod]
    public void Check_ForbiddenTransitivePackage_ReturnsViolationAndEvidencePath()
    {
        var report = GraphQueries.Check(Fixtures.Read(), ["be*"], []);
        Assert.IsFalse(report.Passed);
        Assert.HasCount(1, report.Violations);
        Assert.AreEqual("Beta", report.Violations[0].Dependency.Id);
        Assert.HasCount(4, report.Violations[0].Path);
    }

    [TestMethod]
    public void Check_ForbiddenProjectPath_MatchesRecordedProject()
    {
        var report = GraphQueries.Check(Fixtures.Read(), [], ["*/App.Core.csproj"]);
        Assert.IsFalse(report.Passed);
        Assert.AreEqual("App.Core", report.Violations.Single().Dependency.Id);
    }

    [TestMethod]
    public void Check_AllowedGraph_PassesWithoutViolations()
    {
        var report = GraphQueries.Check(Fixtures.Read(), ["Other.*"], ["*.Tests.csproj"]);
        Assert.IsTrue(report.Passed);
        Assert.IsEmpty(report.Violations);
    }

    [TestMethod]
    public void Check_MissingRulesOrUncertainDirectDependencies_RefusesSuccess()
    {
        Assert.ThrowsExactly<InspectionException>(() => GraphQueries.Check(Fixtures.Read(), [], []));
        var graph = Fixtures.Read() with { UnresolvedDeclarations = ["Pruned >= 1.0.0"] };
        Assert.ThrowsExactly<InspectionException>(() => GraphQueries.Check(graph, ["Pruned"], []));
    }

    [TestMethod]
    public void Check_UnresolvedMatchingConstraint_RefusesSuccess()
    {
        var json = Fixtures.Modify(j => j["targets"]!["net10.0"]!.AsObject().Remove("Beta/3.0.0"));
        var graph = Fixtures.Read(json);
        Assert.ThrowsExactly<InspectionException>(() => GraphQueries.Check(graph, ["Beta"], []));
        var unrelated = GraphQueries.Check(graph, ["Other.*"], []);
        CollectionAssert.AreEqual(new[] { "Beta" }, unrelated.UnresolvedDependencies.ToArray());
    }
}
