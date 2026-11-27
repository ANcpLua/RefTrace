using System.Globalization;
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

    private Task<(int Exit, string Output, string Error)> RunAsync(params string[] args)
        => Fixtures.RunCliAsync(context, args);

    [TestMethod]
    public async Task Assets_Json_HasVersionedSchemaAndGraphEvidence()
    {
        var result = await RunAsync("assets", AssetsFile(), "--json");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("assets", json.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("net10.0", json.RootElement.GetProperty("data").GetProperty("framework").GetString());
        Assert.AreEqual(4, json.RootElement.GetProperty("data").GetProperty("nodes").GetArrayLength());
    }

    [TestMethod]
    public async Task Packages_Assets_ShowAnalyzerAssetsOrSayTheyWereNotRecorded()
    {
        var unrecorded = await RunAsync("packages", AssetsFile(), "--assets", "--plain");
        Assert.AreEqual(0, unrecorded.Exit);
        Assert.Contains(CliApplication.AnalyzerAssetsNotRecorded, unrecorded.Output);

        var path = Path.Combine(context.TestTempDirectory!, "analyzers.assets.json");
        await File.WriteAllTextAsync(path, Fixtures.WithAnalyzerAssets(), context.CancellationToken);
        var plain = await RunAsync("packages", path, "--assets", "--plain");
        Assert.Contains(string.Join(Environment.NewLine, "  compile: ref/net10.0/Alpha.dll",
            "  analyzer: analyzers/dotnet/roslyn4.8/cs/Alpha.Analyzers.dll"), plain.Output);
        Assert.DoesNotContain(CliApplication.AnalyzerAssetsNotRecorded, plain.Output);
        var json = await RunAsync("packages", path, "--assets", "--json");
        using var document = JsonDocument.Parse(json.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("analyzerAssetsRecorded").GetBoolean());
        var alpha = data.GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "Alpha");
        Assert.AreEqual(2, alpha.GetProperty("analyzerAssets").GetArrayLength());
    }

    [TestMethod]
    public async Task Why_KnownAndMissingDependencies_HaveDistinctExitCodes()
    {
        var path = AssetsFile();
        var found = await RunAsync("why", "Beta", path, "--plain");
        Assert.AreEqual(0, found.Exit);
        Assert.Contains("App.Core/1.0.0 -> Alpha/2.0.0 -> Beta/3.0.0", found.Output);
        var missing = await RunAsync("why", "Absent", path, "--json");
        Assert.AreEqual(1, missing.Exit);
        using var json = JsonDocument.Parse(missing.Output);
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("trace").GetProperty("found").GetBoolean());
    }

    [TestMethod]
    public async Task Check_ViolationAllowedAndInvalidRules_HaveDistinctExitCodes()
    {
        var path = AssetsFile();
        var violation = await RunAsync("check", path, "--forbid-package", "Beta", "--json");
        Assert.AreEqual(1, violation.Exit);
        using var json = JsonDocument.Parse(violation.Output);
        Assert.AreEqual("Beta", json.RootElement.GetProperty("data").GetProperty("violations")[0].GetProperty("dependency").GetProperty("id").GetString());
        Assert.AreEqual(0, (await RunAsync("check", path, "--forbid-package", "Other.*")).Exit);
        Assert.AreEqual(2, (await RunAsync("check", path)).Exit);
    }

    [TestMethod]
    public async Task MissingFileAndUnknownOption_ProduceJsonErrors()
    {
        var missing = await RunAsync("assets", Path.Combine(context.TestTempDirectory!, "missing.json"), "--json");
        Assert.AreEqual(2, missing.Exit);
        using var json = JsonDocument.Parse(missing.Output);
        Assert.AreEqual("error", json.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("", missing.Error);
        var invalid = await RunAsync("assets", "unused.json", "--unknown", "--json");
        Assert.AreEqual(2, invalid.Exit);
        Assert.Contains("invalid-argument", invalid.Output);
    }

    [TestMethod]
    public async Task Assembly_Json_ContainsStaticMetadata()
    {
        var result = await RunAsync("assembly", typeof(CliApplication).Assembly.Location, "--type", "RefTrace.Cli.CliApplication", "--json");
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("RefTrace.Cli", data.GetProperty("identity").GetProperty("name").GetString());
        Assert.AreEqual("RefTrace.Cli.CliApplication", data.GetProperty("types")[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task Help_ListsSupportedCommands()
    {
        var result = await RunAsync("--help");
        Assert.AreEqual(0, result.Exit);
        Assert.Contains("assets [<input>]", result.Output);
        Assert.Contains("review [<input>]", result.Output);
        Assert.Contains("assembly <file>", result.Output);
        var commandHelp = await RunAsync("assembly", "--help");
        Assert.AreEqual(0, commandHelp.Exit);
        Assert.Contains("--type", commandHelp.Output);
        Assert.DoesNotContain("--json <value>", commandHelp.Output);
        Assert.DoesNotContain("--plain <value>", commandHelp.Output);
    }

    [TestMethod]
    [DataRow("help")]
    [DataRow("--help")]
    [DataRow("-h")]
    public async Task Help_RootForms_ShowTheSameConciseOverview(string argument)
    {
        var result = await RunAsync(argument);
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        Assert.AreEqual((await RunAsync()).Output, result.Output);
        Assert.Contains("Usage: ref <command>", result.Output);
        Assert.Contains("Details: ref help <command>", result.Output);
        Assert.DoesNotContain("RefTrace.Cli", result.Output);
        Assert.IsLessThanOrEqualTo(21, result.Output.Split('\n').Length);
    }

    [TestMethod]
    public async Task Help_NestedCommand_MatchesOptionAndShowsRequiredArguments()
    {
        var result = await RunAsync("help", "aot", "publish");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        Assert.AreEqual((await RunAsync("aot", "publish", "--help")).Output, result.Output);
        Assert.Contains("ref aot publish <project>", result.Output);
        Assert.Contains("--rid", result.Output);
        Assert.Contains("--output", result.Output);
    }

    [TestMethod]
    public async Task Help_UnknownCommand_FailsWithoutRunningAnInspection()
    {
        var result = await RunAsync("help", "unknown");
        Assert.AreEqual(2, result.Exit);
        Assert.AreEqual("", result.Output);
        Assert.Contains("Unknown command 'unknown'", result.Error);
    }

    [TestMethod]
    [DataRow("--help")]
    [DataRow("help")]
    public async Task Help_ClearsConflictingOutputOptions(string command)
    {
        var result = await RunAsync(command, "--json", "--plain");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        Assert.DoesNotContain("invalid-input", result.Output);
    }

    [TestMethod]
    public async Task Version_UsesTheLibraryArgumentValidation()
    {
        Assert.AreEqual(0, (await RunAsync("--version")).Exit);
        var result = await RunAsync("--version", "--plain");
        Assert.AreEqual(2, result.Exit);
        Assert.Contains("cannot be combined", result.Error);
    }

    [TestMethod]
    public async Task Version_CommandPrintsWhatTheOptionPrints()
    {
        var option = await RunAsync("--version");
        var command = await RunAsync("version");
        Assert.AreEqual(0, command.Exit);
        Assert.AreEqual("", command.Error);
        Assert.AreEqual(option.Output, command.Output);
        Assert.AreNotEqual("", command.Output.Trim());
        var extra = await RunAsync("version", "unexpected");
        Assert.AreEqual(2, extra.Exit);
        Assert.Contains("unexpected", extra.Error);
        Assert.Contains("version", (await RunAsync("help")).Output);
    }

    [TestMethod]
    public async Task Suggestions_Prefix_ReturnsMatchingCommands()
    {
        var result = await RunAsync("[suggest]", "ass");
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Error);
        string[] expected = ["assembly", "assets"];
        CollectionAssert.AreEquivalent(expected,
            result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    public async Task Check_RepeatedPatterns_AreAllApplied()
    {
        var result = await RunAsync("check", AssetsFile(), "--forbid-package", "Nothing", "--forbid-package", "Beta", "--plain");
        Assert.AreEqual(1, result.Exit);
        Assert.Contains("matches 'Beta'", result.Output);
    }

    [TestMethod]
    public async Task Packages_ReportsOnlyResolvedPackagesAndTheirOrigin()
    {
        var result = await RunAsync("packages", AssetsFile(), "--json");
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
    public async Task Terminal_EscapesArtifactTextAndKeepsPlainAndJsonModesSeparate()
    {
        var path = Path.Combine(context.TestTempDirectory!, "[red]project.assets.json");
        await File.WriteAllTextAsync(path, Fixtures.Assets, context.CancellationToken);
        var rich = await RunAsync("packages", path);
        Assert.AreEqual(0, rich.Exit);
        Assert.Contains("REFTRACE", rich.Output);
        Assert.Contains("Resolved version", rich.Output);
        Assert.Contains("[red]project.assets.json", rich.Output);
        var plain = await RunAsync("packages", path, "--plain");
        Assert.AreEqual(0, plain.Exit);
        Assert.IsFalse(plain.Output.Contains("REFTRACE", StringComparison.Ordinal));
        var incompatible = await RunAsync("packages", path, "--json", "--plain");
        Assert.AreEqual(2, incompatible.Exit);
        using var json = JsonDocument.Parse(incompatible.Output);
        Assert.AreEqual("error", json.RootElement.GetProperty("kind").GetString());
    }

    [TestMethod]
    public async Task Runtime_InvalidPidAndDuration_ProduceJsonErrors()
    {
        Assert.AreEqual(2, (await RunAsync("runtime", "--pid", "0", "--json")).Exit);
        var result = await RunAsync("runtime", "--pid", Environment.ProcessId.ToString(), "--seconds", "0", "--json");
        Assert.AreEqual(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains("--seconds", json.RootElement.GetProperty("data").GetProperty("message").GetString()!);
    }

    [TestMethod]
    [DataRow("29.5", "29")]
    [DataRow("29,5", "29")]
    [DataRow("1", "0.5")]
    [DataRow("1", "0,5")]
    [DataRow("0.25", "0,25")]
    [DataRow("0,25", "0.25")]
    public async Task Runtime_AustrianDecimalDurationsApplyTheTimeoutComparison(string seconds, string timeout)
    {
        await AssertAustrianDurationErrorAsync("--timeout must exceed --seconds",
            "runtime", "--pid", "2147483647", "--seconds", seconds, "--timeout", timeout, "--json");
    }

    [TestMethod]
    [DataRow("1799.5")]
    [DataRow("1799,5")]
    public async Task AotPublish_AustrianDecimalTimeoutBelowLimitReachesOutputValidation(string timeout)
    {
        var project = Path.Combine(context.TestTempDirectory!, "Fixture.csproj");
        await File.WriteAllTextAsync(project, "<Project/>", context.CancellationToken);
        await AssertAustrianDurationErrorAsync("Use a separate output directory",
            "aot", "publish", project, "--rid", "osx-arm64", "--output", Path.GetDirectoryName(project)!,
            "--timeout", timeout, "--json");
    }

    [TestMethod]
    [DataRow("--seconds", "1.2,3")]
    [DataRow("--timeout", "1,2,3")]
    [DataRow("--timeout", "1e99")]
    public async Task Runtime_AustrianInvalidDurationNamesTheOptionInJson(string option, string value)
    {
        await AssertAustrianDurationErrorAsync(option, "runtime", "--pid", "0", option, value, "--json");
    }

    private async Task AssertAustrianDurationErrorAsync(string messageFragment, params string[] args)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            var result = await RunAsync(args);
            Assert.AreEqual(2, result.Exit);
            Assert.AreEqual("", result.Error);
            using var json = JsonDocument.Parse(result.Output);
            Assert.AreEqual("error", json.RootElement.GetProperty("kind").GetString());
            var data = json.RootElement.GetProperty("data");
            Assert.AreEqual("invalid-argument", data.GetProperty("code").GetString());
            Assert.Contains(messageFragment, data.GetProperty("message").GetString()!);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public async Task Reflection_FindingsAreAnExplicitPolicyNotAnImplicitFailure()
    {
        var file = typeof(ReflectionSamples).Assembly.Location;
        var report = await RunAsync("reflection", file, "--json");
        Assert.AreEqual(0, report.Exit);
        using var json = JsonDocument.Parse(report.Output);
        Assert.AreEqual("reflection", json.RootElement.GetProperty("kind").GetString());
        Assert.IsGreaterThan(0, json.RootElement.GetProperty("data").GetProperty("findings").GetArrayLength());
        Assert.AreEqual(1, (await RunAsync("reflection", file, "--fail-on-findings", "--json")).Exit);
    }

    [TestMethod]
    public async Task AotInspect_CompatibilityDeclarationRemainsUnproven()
    {
        var file = Path.Combine(context.TestTempDirectory!, "claimed-compatible.dll");
        await using (var stream = SafetyPe.Create([0x2a], true)) await File.WriteAllBytesAsync(file, stream.ToArray(), context.CancellationToken);
        var result = await RunAsync("aot", "inspect", file, "--json");
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("not-established", data.GetProperty("assessment").GetString());
        Assert.IsTrue(data.GetProperty("declaredAotCompatible").GetBoolean());
    }

    [TestMethod]
    public async Task AotPublish_InvalidProjectDoesNotCreateBuildOutput()
    {
        var output = Path.Combine(context.TestTempDirectory!, "must-not-exist");
        var result = await RunAsync("aot", "publish", "missing.csproj", "--rid", "osx-arm64", "--output", output, "--json");
        Assert.AreEqual(2, result.Exit);
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public async Task Assembly_Json_PreservesMetadataAndDebugEvidence()
    {
        var file = Path.Combine(context.TestTempDirectory!, "metadata.dll");
        await using (var image = SafetyPe.Create([0x2a], declaredAot: true, debug: true))
            await File.WriteAllBytesAsync(file, image.ToArray(), context.CancellationToken);
        var result = await RunAsync("assembly", file, "--json");
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("True", data.GetProperty("assemblyMetadata").GetProperty("IsAotCompatible").GetString());
        Assert.IsTrue(data.GetProperty("debugDirectoryKinds").EnumerateArray().Any(x => x.GetString() == "CodeView"));
    }
}
