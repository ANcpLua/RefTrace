using System.Text.Json;
using RefTrace.Core;

namespace RefTrace.Build;

public sealed record ProjectInput(string? ProjectPath, string AssetsPath, string? SdkVersion = null);

public static class ProjectInputs
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", ".idea", ".venv", "node_modules", "bin", "obj", "artifacts" };

    public static IReadOnlyList<string> FindProjects(string input)
    {
        var path = Path.GetFullPath(input);
        if (File.Exists(path))
        {
            if (!IsProject(path)) throw new InspectionException("invalid-argument", "Expected a .csproj, .fsproj, .vbproj or project directory.", path);
            return [path];
        }
        if (!Directory.Exists(path)) throw new InspectionException("path-not-found", "The project input does not exist.", path);
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            files.AddRange(Directory.EnumerateFiles(directory).Where(IsProject));
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
        }
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    public static async Task<ProjectInput> ResolveAsync(string input, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(input);
        if (!IsProject(path) && !Directory.Exists(path))
        {
            if (!File.Exists(path)) throw new InspectionException("file-not-found", "The input file does not exist.", path);
            return new(null, path);
        }
        var projects = FindProjects(path);
        if (projects.Count == 0) throw new InspectionException("project-not-found", "No .NET project was found in this directory.", path);
        if (projects.Count != 1) throw new InspectionException("ambiguous-project", "Choose one project, or use ref review to inspect the whole directory.", path, projects);
        return await EvaluateAsync(projects[0], cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ProjectInput> EvaluateAsync(string project, CancellationToken cancellationToken = default)
    {
        project = Path.GetFullPath(project);
        var directory = Path.GetDirectoryName(project)!;
        var evaluation = await ProcessCapture.RunAsync("dotnet",
            ["msbuild", project, "-nologo", "-verbosity:quiet", "-getProperty:ProjectAssetsFile,NETCoreSdkVersion"],
            directory, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        if (evaluation.ExitCode != 0)
            throw new InspectionException("evaluation-failed", evaluation.Error.Trim() + evaluation.Output.Trim(), project);
        using var document = JsonDocument.Parse(evaluation.Output);
        if (!document.RootElement.TryGetProperty("Properties", out var properties)
            || !properties.TryGetProperty("ProjectAssetsFile", out var assetsProperty)
            || string.IsNullOrWhiteSpace(assetsProperty.GetString()))
            throw new InspectionException("evaluation-failed", "MSBuild did not identify a restore assets file.", project);
        var assets = Path.GetFullPath(assetsProperty.GetString()!, directory);
        if (!File.Exists(assets)) throw new InspectionException("restore-required", "Restore this project before inspecting its dependencies.", assets);
        var stream = File.OpenRead(assets);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var restored = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!restored.RootElement.TryGetProperty("project", out var metadata)
            || !metadata.TryGetProperty("restore", out var restore)
            || !restore.TryGetProperty("projectPath", out var recorded)
            || string.IsNullOrWhiteSpace(recorded.GetString())
            || !SamePath(Path.GetFullPath(recorded.GetString()!, directory), project))
            throw new InspectionException("mismatched-assets", "The resolved assets file does not identify this project. Restore the project again.", assets);
        return new(project, assets, properties.TryGetProperty("NETCoreSdkVersion", out var sdk) ? sdk.GetString() : null);
    }

    public static async Task<string> ResolveSdkAsync(string input, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(input);
        var directory = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        if (!Directory.Exists(directory)) throw new InspectionException("directory-not-found", "SDK selection directory does not exist.", directory);
        if (File.Exists(Path.Combine(directory, "dotnet.dll")) && Directory.Exists(Path.Combine(directory, "Sdks"))) return directory;
        var version = await ProcessCapture.RunAsync("dotnet", ["--version"], directory, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (version.ExitCode != 0) throw new InspectionException("sdk-unavailable", version.Error.Trim() + version.Output.Trim(), directory);
        var installed = await ProcessCapture.RunAsync("dotnet", ["--list-sdks"], directory, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        var prefix = version.Output.Trim() + " [";
        var entries = installed.Output.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(']')).Distinct(StringComparer.Ordinal).ToArray();
        if (installed.ExitCode != 0 || entries.Length == 0) throw new InspectionException("sdk-unavailable", "The selected SDK could not be located in dotnet --list-sdks.", directory);
        if (entries.Length != 1) throw new InspectionException("ambiguous-sdk", "More than one installation matches the selected SDK. Supply its directory explicitly.", directory, entries);
        var entry = entries[0];
        return Path.Combine(entry[prefix.Length..^1], version.Output.Trim());
    }

    private static bool IsProject(string path) => Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj";
    private static bool SamePath(string first, string second) => string.Equals(first, second,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
