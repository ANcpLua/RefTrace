using System.Text;
using System.Text.Json.Nodes;
using RefTrace.Cli;
using RefTrace.Core;

namespace RefTrace.Tests;

internal static class Fixtures
{
    internal const string Assets = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "App.Core/1.0.0": { "type": "project", "dependencies": { "Alpha": "2.0.0" }, "compile": { "bin/App.Core.dll": {} } },
              "Alpha/2.0.0": { "type": "package", "dependencies": { "Beta": "3.0.0" }, "compile": { "ref/net10.0/Alpha.dll": {} }, "runtime": { "lib/net10.0/Alpha.dll": {} } },
              "Beta/3.0.0": { "type": "package" }
            }
          },
          "libraries": {
            "App.Core/1.0.0": { "type": "project", "path": "../App.Core/App.Core.csproj", "msbuildProject": "../App.Core/App.Core.csproj" },
            "Alpha/2.0.0": { "type": "package", "path": "alpha/2.0.0", "sha512": "", "files": [] },
            "Beta/3.0.0": { "type": "package", "path": "beta/3.0.0", "sha512": "", "files": [] }
          },
          "projectFileDependencyGroups": { "net10.0": ["App.Core >= 1.0.0"] }
        }
        """;

    // Analyzer assets as NuGet 8.0 records them when a project sets RestoreEnableAnalyzerAssets.
    internal static string WithAnalyzerAssets() => Modify(j =>
    {
        j["targets"]!["net10.0"]!["Alpha/2.0.0"]!["analyzers"] = JsonNode.Parse("""
            {
              "analyzers/dotnet/roslyn5.0/cs/Alpha.Analyzers.dll": { "codeLanguage": "cs", "compilerApiVersion": "roslyn5.0" },
              "analyzers/dotnet/roslyn4.8/cs/Alpha.Analyzers.dll": { "codeLanguage": "cs", "compilerApiVersion": "roslyn4.8" }
            }
            """);
        j["project"] = JsonNode.Parse("""
            { "version": "1.0.0", "restore": { "projectUniqueName": "/app/App.csproj", "projectPath": "/app/App.csproj", "projectName": "App", "restoreEnableAnalyzerAssets": true, "originalTargetFrameworks": ["net10.0"], "frameworks": { "net10.0": { "targetAlias": "net10.0" } } }, "frameworks": { "net10.0": { "framework": "net10.0", "targetAlias": "net10.0" } } }
            """);
    });

    internal static DependencyGraph Read(string json = Assets, string? target = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AssetsInspector.Read(stream, "/fixture/project.assets.json", target);
    }

    internal static string Modify(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(Assets)!.AsObject();
        change(json);
        return json.ToJsonString();
    }

    internal static async Task<(int Exit, string Output, string Error)> RunCliAsync(TestContext context, params string[] arguments)
    {
        await using var output = new StringWriter();
        await using var error = new StringWriter();
        var exit = await CliApplication.RunAsync(arguments, output, error, context.CancellationToken);
        return (exit, output.ToString(), error.ToString());
    }
}
