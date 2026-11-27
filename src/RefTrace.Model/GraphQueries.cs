using System.IO.Enumeration;

namespace RefTrace.Core;

public static class GraphQueries
{
    public static DependencyTrace Explain(DependencyGraph graph, string dependency)
    {
        var path = new DependencyPaths(graph).Find(dependency);
        return new(dependency, path.Count != 0, path);
    }

    // One breadth-first search per graph query. Later matches resume from the existing frontier.
    private sealed class DependencyPaths(DependencyGraph graph)
    {
        private readonly Dictionary<string, DependencyNode> _nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _previous = new(StringComparer.OrdinalIgnoreCase) { [graph.RootId] = null };
        private readonly Queue<string> _pending = new([graph.RootId]);
        private ILookup<string, DependencyEdge>? _outgoing;

        public IReadOnlyList<DependencyNode> Find(string destination)
        {
            if (!_nodes.ContainsKey(destination)) return [];
            while (!_previous.ContainsKey(destination))
            {
                if (!_pending.TryDequeue(out var current)) return [];
                _outgoing ??= graph.Edges.ToLookup(e => e.From, StringComparer.OrdinalIgnoreCase);
                foreach (var edge in _outgoing[current].OrderBy(e => e.To, StringComparer.OrdinalIgnoreCase))
                    if (_previous.TryAdd(edge.To, current)) _pending.Enqueue(edge.To);
            }
            var path = new List<DependencyNode>();
            for (string? id = destination; id is not null; id = _previous[id])
                path.Add(_nodes[id]);
            path.Reverse();
            return path.ToArray();
        }
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
        DependencyPaths? paths = null;
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
                {
                    paths ??= new DependencyPaths(graph);
                    violations.Add(new(pattern, node, paths.Find(node.Id)));
                }
            }
        }
        return new(graph.SourcePath, graph.Target, violations.Count == 0, violations.ToArray(), unresolved);
    }
}
