using System.Text;
using System.Text.Json.Nodes;
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
}
