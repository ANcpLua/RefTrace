using System.Text.Json;
using RefTrace.Cli;

namespace RefTrace.Tests;

[TestClass]
public sealed class CliTests(TestContext context)
{
    private string AssetsFile()
    {
        var path = Path.Combine(context.TestTempDirectory!, "project.assets.json");
        File.WriteAllText(path, Fixtures.Assets);
        return path;
    }

    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        return (CliApplication.Run(args, output, error), output.ToString(), error.ToString());
    }

    [TestMethod]
    public void Assets_Json_HasVersionedSchemaAndGraphEvidence()
    {
        var result = Run("assets", AssetsFile(), "--json");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("assets", json.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("net10.0", json.RootElement.GetProperty("data").GetProperty("framework").GetString());
        Assert.AreEqual(4, json.RootElement.GetProperty("data").GetProperty("nodes").GetArrayLength());
    }

    [TestMethod]
    public void Why_KnownAndMissingDependencies_HaveDistinctExitCodes()
    {
        var path = AssetsFile();
        var found = Run("why", path, "Beta", "--plain");
        Assert.AreEqual(0, found.Exit);
        Assert.Contains("App.Core/1.0.0 -> Alpha/2.0.0 -> Beta/3.0.0", found.Output);
        var missing = Run("why", path, "Absent", "--json");
        Assert.AreEqual(1, missing.Exit);
        using var json = JsonDocument.Parse(missing.Output);
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("trace").GetProperty("found").GetBoolean());
    }

    [TestMethod]
    public void Check_ViolationAllowedAndInvalidRules_HaveDistinctExitCodes()
    {
        var path = AssetsFile();
        var violation = Run("check", path, "--forbid-package", "Beta", "--json");
        Assert.AreEqual(1, violation.Exit);
        using var json = JsonDocument.Parse(violation.Output);
        Assert.AreEqual("Beta", json.RootElement.GetProperty("data").GetProperty("violations")[0].GetProperty("dependency").GetProperty("id").GetString());
        Assert.AreEqual(0, Run("check", path, "--forbid-package", "Other.*").Exit);
        Assert.AreEqual(2, Run("check", path).Exit);
    }

    [TestMethod]
    public void MissingFileAndUnknownOption_ProduceJsonErrors()
    {
        var missing = Run("assets", Path.Combine(context.TestTempDirectory!, "missing.json"), "--json");
        Assert.AreEqual(2, missing.Exit);
        using var json = JsonDocument.Parse(missing.Output);
        Assert.AreEqual("error", json.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("", missing.Error);
        var invalid = Run("assets", "unused.json", "--unknown", "--json");
        Assert.AreEqual(2, invalid.Exit);
        Assert.Contains("invalid-input", invalid.Output);
    }

    [TestMethod]
    public void Assembly_Json_ContainsStaticMetadata()
    {
        var result = Run("assembly", typeof(CliApplication).Assembly.Location, "--type", "RefTrace.Cli.CliApplication", "--json");
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("RefTrace.Cli", data.GetProperty("identity").GetProperty("name").GetString());
        Assert.AreEqual("RefTrace.Cli.CliApplication", data.GetProperty("types")[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public void Help_ListsSupportedCommands()
    {
        var result = Run("--help");
        Assert.AreEqual(0, result.Exit);
        Assert.Contains("assets <file>", result.Output);
        Assert.Contains("assembly <file>", result.Output);
        var commandHelp = Run("assembly", "--help");
        Assert.AreEqual(0, commandHelp.Exit);
        Assert.Contains("--type", commandHelp.Output);
        Assert.DoesNotContain("--json <value>", commandHelp.Output);
        Assert.DoesNotContain("--plain <value>", commandHelp.Output);
    }

    [TestMethod]
    [DataRow("help")]
    [DataRow("--help")]
    [DataRow("-h")]
    public void Help_RootForms_ShowTheSameConciseOverview(string argument)
    {
        var result = Run(argument);
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        Assert.AreEqual(Run().Output, result.Output);
        Assert.Contains("Usage: ref <command>", result.Output);
        Assert.Contains("Details: ref help <command>", result.Output);
        Assert.DoesNotContain("RefTrace.Cli", result.Output);
        Assert.IsLessThanOrEqualTo(20, result.Output.Split('\n').Length);
    }

    [TestMethod]
    public void Help_NestedCommand_MatchesOptionAndShowsRequiredArguments()
    {
        var result = Run("help", "aot", "publish");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        Assert.AreEqual(Run("aot", "publish", "--help").Output, result.Output);
        Assert.Contains("Usage: ref aot publish <project>", result.Output);
        Assert.Contains("--rid", result.Output);
        Assert.Contains("--output", result.Output);
    }

    [TestMethod]
    public void Help_UnknownCommand_FailsWithoutRunningAnInspection()
    {
        var result = Run("help", "unknown");
        Assert.AreEqual(2, result.Exit);
        Assert.AreEqual("", result.Output);
        Assert.Contains("Unknown command 'unknown'", result.Error);
    }

    [TestMethod]
    public void Suggestions_Prefix_ReturnsMatchingCommands()
    {
        var result = Run("[suggest]", "ass");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        string[] expected = ["assembly", "assets"];
        CollectionAssert.AreEquivalent(expected,
            result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    public void Check_RepeatedPatterns_AreAllApplied()
    {
        var result = Run("check", AssetsFile(), "--forbid-package", "Nothing", "--forbid-package", "Beta", "--plain");
        Assert.AreEqual(1, result.Exit);
        Assert.Contains("matches 'Beta'", result.Output);
    }

    [TestMethod]
    public void Packages_ReportsOnlyResolvedPackagesAndTheirOrigin()
    {
        var result = Run("packages", AssetsFile(), "--json");
        Assert.AreEqual(0, result.Exit);
        Assert.IsFalse(result.Output.Contains('\u001b'));
        using var json = JsonDocument.Parse(result.Output);
        var packages = json.RootElement.GetProperty("data").GetProperty("packages");
        Assert.AreEqual(2, packages.GetArrayLength());
        Assert.AreEqual("Alpha", packages[0].GetProperty("id").GetString());
        Assert.AreEqual("2.0.0", packages[0].GetProperty("version").GetString());
        Assert.IsFalse(packages[0].GetProperty("direct").GetBoolean());
        Assert.IsFalse(packages.EnumerateArray().Any(p => p.GetProperty("id").GetString() == "App.Core"));
    }

    [TestMethod]
    public void Terminal_EscapesArtifactTextAndKeepsPlainAndJsonModesSeparate()
    {
        var path = Path.Combine(context.TestTempDirectory!, "[red]project.assets.json");
        File.WriteAllText(path, Fixtures.Assets);
        var rich = Run("packages", path);
        Assert.AreEqual(0, rich.Exit);
        Assert.Contains("REFTRACE", rich.Output);
        Assert.Contains("Resolved version", rich.Output);
        Assert.Contains("[red]project.assets.json", rich.Output);
        var plain = Run("packages", path, "--plain");
        Assert.AreEqual(0, plain.Exit);
        Assert.IsFalse(plain.Output.Contains("REFTRACE", StringComparison.Ordinal));
        var incompatible = Run("packages", path, "--json", "--plain");
        Assert.AreEqual(2, incompatible.Exit);
        using var json = JsonDocument.Parse(incompatible.Output);
        Assert.AreEqual("error", json.RootElement.GetProperty("kind").GetString());
    }

    [TestMethod]
    public void Runtime_InvalidPidAndDuration_ProduceJsonErrors()
    {
        Assert.AreEqual(2, Run("runtime", "--pid", "0", "--json").Exit);
        var result = Run("runtime", "--pid", Environment.ProcessId.ToString(), "--seconds", "0", "--json");
        Assert.AreEqual(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains("--seconds", json.RootElement.GetProperty("data").GetProperty("message").GetString()!);
    }

    [TestMethod]
    public void Reflection_FindingsAreAnExplicitPolicyNotAnImplicitFailure()
    {
        var file = typeof(ReflectionSamples).Assembly.Location;
        var report = Run("reflection", file, "--json");
        Assert.AreEqual(0, report.Exit);
        using var json = JsonDocument.Parse(report.Output);
        Assert.AreEqual("reflection", json.RootElement.GetProperty("kind").GetString());
        Assert.IsGreaterThan(0, json.RootElement.GetProperty("data").GetProperty("findings").GetArrayLength());
        Assert.AreEqual(1, Run("reflection", file, "--fail-on-findings", "--json").Exit);
    }

    [TestMethod]
    public void AotInspect_CompatibilityDeclarationRemainsUnproven()
    {
        var file = Path.Combine(context.TestTempDirectory!, "claimed-compatible.dll");
        using (var stream = SafetyPe.Create([0x2a], true)) File.WriteAllBytes(file, stream.ToArray());
        var result = Run("aot", "inspect", file, "--json");
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("not-established", data.GetProperty("assessment").GetString());
        Assert.IsTrue(data.GetProperty("declaredAotCompatible").GetBoolean());
    }

    [TestMethod]
    public void AotPublish_InvalidProjectDoesNotCreateBuildOutput()
    {
        var output = Path.Combine(context.TestTempDirectory!, "must-not-exist");
        var result = Run("aot", "publish", "missing.csproj", "--rid", "osx-arm64", "--output", output, "--json");
        Assert.AreEqual(2, result.Exit);
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public void Assembly_Json_PreservesMetadataAndDebugEvidence()
    {
        var file = Path.Combine(context.TestTempDirectory!, "metadata.dll");
        using (var image = SafetyPe.Create([0x2a], declaredAot: true, debug: true))
            File.WriteAllBytes(file, image.ToArray());
        var result = Run("assembly", file, "--json");
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("True", data.GetProperty("assemblyMetadata").GetProperty("IsAotCompatible").GetString());
        Assert.IsTrue(data.GetProperty("debugDirectoryKinds").EnumerateArray().Any(x => x.GetString() == "CodeView"));
    }
}
