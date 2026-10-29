using System.IO.Enumeration;

namespace RefTrace.Core;

public static class GraphQueries
{
    public static DependencyTrace Explain(DependencyGraph graph, string dependency)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        if (!nodes.TryGetValue(dependency, out var destination))
            return new(dependency, false, []);

        var outgoing = graph.Edges.ToLookup(e => e.From, StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [graph.RootId] = null };
        var queue = new Queue<string>();
        queue.Enqueue(graph.RootId);
        while (queue.TryDequeue(out var current))
        {
            if (string.Equals(current, destination.Id, StringComparison.OrdinalIgnoreCase))
            {
                var path = new List<DependencyNode>();
                for (string? id = current; id is not null; id = previous[id])
                    path.Add(nodes[id]);
                path.Reverse();
                return new(dependency, true, path.ToArray());
            }
            foreach (var edge in outgoing[current].OrderBy(e => e.To, StringComparer.OrdinalIgnoreCase))
            {
                if (previous.TryAdd(edge.To, current)) queue.Enqueue(edge.To);
            }
        }
        return new(dependency, false, []);
    }

    public static PolicyReport Check(DependencyGraph graph,
        IReadOnlyList<string> forbiddenPackages, IReadOnlyList<string> forbiddenProjects)
    {
        if (forbiddenPackages.Count + forbiddenProjects.Count == 0
            || forbiddenPackages.Concat(forbiddenProjects).Any(string.IsNullOrWhiteSpace))
            throw new InspectionException("invalid-argument", "Specify at least one nonempty --forbid-package or --forbid-project pattern.");
        if (graph.UnresolvedDeclarations.Count != 0)
            throw new InspectionException("inconclusive-policy", "Cannot certify dependency isolation: some declared dependencies are absent from the selected target (possibly pruned). Inspect unresolvedDeclarations first.");
        var unresolved = graph.Nodes.Where(n => n.Kind == "unresolved").Select(n => n.Id)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (unresolved.Any(id => forbiddenPackages.Concat(forbiddenProjects)
            .Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, id, ignoreCase: true))))
            throw new InspectionException("inconclusive-policy", "A forbidden pattern matches an unresolved dependency constraint. No pass/fail conclusion can be drawn for that dependency.");

        var violations = new List<PolicyViolation>();
        foreach (var node in graph.Nodes)
        {
            var patterns = node.Kind switch
            {
                "package" => forbiddenPackages,
                "project" => forbiddenProjects,
                _ => []
            };
            foreach (var pattern in patterns)
            {
                var matches = FileSystemName.MatchesSimpleExpression(pattern, node.Id, ignoreCase: true);
                if (!matches && node is { Kind: "project", ProjectPath: not null })
                    matches = FileSystemName.MatchesSimpleExpression(pattern.Replace('\\', '/'),
                        node.ProjectPath.Replace('\\', '/'), ignoreCase: OperatingSystem.IsWindows());
                if (matches)
                    violations.Add(new(pattern, node, Explain(graph, node.Id).Path));
            }
        }
        return new(graph.SourcePath, graph.Target, violations.Count == 0, violations.ToArray(), unresolved);
    }
}
