using NuGet.Frameworks;
using NuGet.ProjectModel;

namespace RefTrace.Core;

public static class AssetsInspector
{
    public static DependencyGraph Read(string path, string? target = null)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return Read(stream, fullPath, target);
    }

    public static DependencyGraph Read(Stream stream, string sourcePath, string? target = null)
    {
        var file = new LockFileFormat().Read(stream, sourcePath);
        if (file.Version is not (3 or 4) || file.Targets.Count == 0)
            throw new InspectionException("invalid-artifact", "Expected a restored project.assets.json in supported format 3 or 4 with at least one target.");
        if (file.LogMessages.Any(m => m.Level == NuGet.Common.LogLevel.Error))
            throw new InspectionException("restore-failed", "The assets file records a failed restore. Complete a successful restore before inspection.");

        var matches = file.Targets.Where(t => target is null || MatchesTarget(t, target)).ToArray();
        if (matches.Length != 1)
        {
            var choices = file.Targets.Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();
            throw new InspectionException(matches.Length == 0 ? "target-not-found" : "ambiguous-target",
                "Select exactly one recorded target with --framework.", sourcePath, choices);
        }

        var selected = matches[0];
        var framework = selected.TargetFramework.GetShortFolderName();
        var projectPath = file.PackageSpec?.RestoreMetadata?.ProjectPath
            ?? file.PackageSpec?.FilePath ?? sourcePath;
        const string rootId = "$project";
        var nodes = new List<DependencyNode>
        {
            new(rootId, "", "root", projectPath, [], [])
        };
        var edges = new List<DependencyEdge>();
        var unresolved = new List<string>();
        // Restore records analyzer assets only when the project sets RestoreEnableAnalyzerAssets.
        // Without it, an empty list would wrongly claim that a package has no analyzers.
        var analyzersRecorded = file.PackageSpec?.RestoreMetadata?.RestoreEnableAnalyzerAssets == true
            || selected.Libraries.Any(l => l.AnalyzerAssets.Count != 0);
        var libraries = file.Libraries.ToDictionary(l => Key(l.Name, l.Version.ToNormalizedString()), StringComparer.OrdinalIgnoreCase);

        foreach (var library in selected.Libraries.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(library.Name) || library.Version is null)
                throw new InspectionException("invalid-artifact", $"A library in target '{selected.Name}' has no valid name or version.");
            libraries.TryGetValue(Key(library.Name, library.Version.ToNormalizedString()), out var metadata);
            var kind = library.Type ?? metadata?.Type ?? "unknown";
            nodes.Add(new DependencyNode(
                library.Name, library.Version.ToNormalizedString(), kind,
                kind == "project" ? metadata?.MSBuildProject ?? metadata?.Path : null,
                library.CompileTimeAssemblies.Select(a => a.Path).Order(StringComparer.Ordinal).ToArray(),
                library.RuntimeAssemblies.Select(a => a.Path).Order(StringComparer.Ordinal).ToArray(),
                analyzersRecorded ? library.AnalyzerAssets.Select(a => a.Path).Order(StringComparer.Ordinal).ToArray() : null));
            foreach (var dependency in library.Dependencies)
                edges.Add(new(library.Name, dependency.Id, dependency.VersionRange.ToNormalizedString()));
        }

        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            // NuGet can retain dependency constraints whose libraries were excluded
            // by project-reference asset filtering. Preserve them as unresolved evidence.
            if (nodeIds.Add(edge.To))
                nodes.Add(new(edge.To, "", "unresolved", null, [], []));
        }

        var groups = file.ProjectFileDependencyGroups.Where(g =>
            string.IsNullOrEmpty(g.FrameworkName)
            || string.Equals(g.FrameworkName, selected.TargetAlias, StringComparison.OrdinalIgnoreCase)
            || NuGetFramework.Parse(g.FrameworkName).Equals(selected.TargetFramework)).ToArray();
        if (groups.Length == 0)
            throw new InspectionException("invalid-artifact", $"No direct-dependency group was recorded for target '{selected.Name}'.");

        foreach (var declaration in groups.SelectMany(g => g.Dependencies).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var separator = declaration.IndexOf(' ');
            var id = separator < 0 ? declaration : declaration[..separator];
            if (nodes.Any(n => n.Kind != "unresolved" && string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase)))
                edges.Add(new(rootId, id, separator < 0 ? "" : declaration[(separator + 1)..]));
            else
                unresolved.Add(declaration);
        }

        return new DependencyGraph(sourcePath, projectPath, selected.Name, framework,
            string.IsNullOrEmpty(selected.RuntimeIdentifier) ? null : selected.RuntimeIdentifier,
            rootId, nodes.ToArray(), edges.OrderBy(e => e.From, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.To, StringComparer.OrdinalIgnoreCase).ToArray(),
            unresolved.Order(StringComparer.OrdinalIgnoreCase).ToArray()) { AnalyzerAssetsRecorded = analyzersRecorded };
    }

    private static bool MatchesTarget(LockFileTarget target, string selection)
    {
        var suffix = string.IsNullOrEmpty(target.RuntimeIdentifier) ? "" : "/" + target.RuntimeIdentifier;
        return string.Equals(target.Name, selection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(target.TargetFramework.GetShortFolderName() + suffix, selection, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(target.TargetAlias)
                && string.Equals(target.TargetAlias + suffix, selection, StringComparison.OrdinalIgnoreCase));
    }

    private static string Key(string name, string version) => name + "/" + version;
}
