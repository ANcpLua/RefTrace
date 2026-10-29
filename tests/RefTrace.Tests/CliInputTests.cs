using System.Text.Json;
using System.Text.Json.Nodes;
using RefTrace.Cli;

namespace RefTrace.Tests;

[TestClass]
public sealed class CliInputTests(TestContext context)
{
    private async Task<(int Exit, string Output, string Error)> Run(params string[] arguments)
    {
        await using var output = new StringWriter();
        await using var error = new StringWriter();
        var exit = await CliApplication.RunAsync(arguments, output, error, context.CancellationToken);
        return (exit, output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task Packages_DefaultIsCompactAndFiltersComposeWithAssetDetails()
    {
        var path = Path.Combine(context.TestTempDirectory!, "packages.json");
        await File.WriteAllTextAsync(path, Fixtures.Modify(root =>
            root["projectFileDependencyGroups"]!["net10.0"] = new JsonArray("Alpha >= 2.0.0")), context.CancellationToken);
        var compact = await Run("packages", path, "--json");
        Assert.AreEqual(0, compact.Exit);
        Assert.AreEqual("", compact.Error);
        Assert.AreEqual(1, compact.Output.TrimEnd().Split('\n').Length);
        using var defaultJson = JsonDocument.Parse(compact.Output);
        var packages = defaultJson.RootElement.GetProperty("data").GetProperty("packages");
        Assert.AreEqual(2, packages.GetArrayLength());
        Assert.IsFalse(packages[0].TryGetProperty("compileAssets", out _));
        var detailed = await Run("packages", path, "--package", "a*", "--direct", "--assets", "--json");
        Assert.AreEqual(0, detailed.Exit);
        using var json = JsonDocument.Parse(detailed.Output);
        var selected = json.RootElement.GetProperty("data").GetProperty("packages");
        Assert.AreEqual(1, selected.GetArrayLength());
        Assert.AreEqual("Alpha", selected[0].GetProperty("id").GetString());
        Assert.IsTrue(selected[0].GetProperty("direct").GetBoolean());
        Assert.AreEqual("ref/net10.0/Alpha.dll", selected[0].GetProperty("compileAssets")[0].GetString());
        var absent = await Run("packages", path, "--package", "Beta", "--direct", "--json");
        using var absentJson = JsonDocument.Parse(absent.Output);
        Assert.AreEqual(0, absentJson.RootElement.GetProperty("data").GetProperty("packages").GetArrayLength());
        var plain = await Run("packages", path, "--package", "Alpha", "--assets", "--plain");
        Assert.Contains("ref/net10.0/Alpha.dll", plain.Output);
    }

    [TestMethod]
    public async Task AmbiguousTargets_ReportChoicesInJsonAndPlain()
    {
        var path = Path.Combine(context.TestTempDirectory!, "targets.json");
        await File.WriteAllTextAsync(path, Fixtures.Modify(root =>
        {
            root["targets"]!["net11.0"] = root["targets"]!["net10.0"]!.DeepClone();
            root["projectFileDependencyGroups"]!["net11.0"] = root["projectFileDependencyGroups"]!["net10.0"]!.DeepClone();
        }), context.CancellationToken);
        var result = await Run("packages", path, "--json");
        Assert.AreEqual(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("ambiguous-target", data.GetProperty("code").GetString());
        Assert.AreEqual(path, data.GetProperty("input").GetString());
        CollectionAssert.AreEquivalent(new[] { "net10.0", "net11.0" }, data.GetProperty("candidates").EnumerateArray().Select(t => t.GetString()).ToArray());
        var plain = await Run("packages", path, "--plain");
        Assert.AreEqual("", plain.Output);
        Assert.Contains("ambiguous-target", plain.Error);
        Assert.Contains("net11.0", plain.Error);
        var selected = await Run("packages", path, "-f", "net11.0", "--json");
        Assert.AreEqual(0, selected.Exit);
        using var selectedJson = JsonDocument.Parse(selected.Output);
        Assert.AreEqual("net11.0", selectedJson.RootElement.GetProperty("data").GetProperty("target").GetString());
    }

    [TestMethod]
    public async Task AmbiguousProjects_ReportUsablePathsWithoutEvaluatingThem()
    {
        var root = context.TestTempDirectory!;
        var first = Path.Combine(root, "First.csproj");
        var second = Path.Combine(root, "Second.csproj");
        await File.WriteAllTextAsync(first, "not even XML", context.CancellationToken);
        await File.WriteAllTextAsync(second, "not even XML", context.CancellationToken);
        var result = await Run("packages", root, "--json");
        Assert.AreEqual(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual("ambiguous-project", json.RootElement.GetProperty("data").GetProperty("code").GetString());
        CollectionAssert.AreEqual(new[] { first, second }, json.RootElement.GetProperty("data").GetProperty("candidates").EnumerateArray().Select(p => p.GetString()).ToArray());
    }

    [TestMethod]
    public async Task Review_RejectsConflictingModesBeforeCreatingOutput()
    {
        var output = Path.Combine(context.TestTempDirectory!, "unused");
        var result = await Run("review", "--offline", "--refresh", "--output", output, "--json");
        Assert.AreEqual(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual("invalid-argument", json.RootElement.GetProperty("data").GetProperty("code").GetString());
        Assert.IsFalse(Directory.Exists(output));
        var help = await Run("review", "--help");
        Assert.Contains("--catalog-only", help.Output);
        Assert.Contains("--probes", help.Output);
    }
}
