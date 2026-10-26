using System.Globalization;
using RefTrace.Core;
using RefTrace.Runtime;
using Spectre.Console;
using RefTrace.Metadata;
using RefTrace.Build;

namespace RefTrace.Cli;

internal static class TerminalRenderer
{
    public static void Render(string kind, object data, TextWriter writer, bool modules = false)
    {
        var console = Create(writer);
        console.Write(new Rule($"[bold cyan]REFTRACE[/] [dim]· {Markup.Escape(kind.ToUpperInvariant())}[/]").LeftJustified());
        switch (data)
        {
            case PackageReleaseReport releases:
                Evidence(console, releases.SourcePath, "NuGet version ordering");
                var releaseTable = Table("Version", "Value");
                releaseTable.AddRow(Text("Current"), Text(releases.Current));
                releaseTable.AddRow(Text("Latest stable"), Text(releases.LatestStable ?? "none"));
                releaseTable.AddRow(Text("Latest prerelease"), Text(releases.LatestPrerelease ?? "none"));
                releaseTable.AddRow(Text("Selected"), Text(releases.Selected));
                console.Write(releaseTable);
                Note(console, releases.Selection);
                break;
            case DependencyGraph graph:
                Evidence(console, graph.SourcePath, graph.Target);
                var direct = graph.Edges.Where(e => e.From == graph.RootId).Select(e => e.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nodes = Table("Scope", "Kind", "Dependency", "Version");
                foreach (var node in graph.Nodes.Where(n => n.Id != graph.RootId))
                    nodes.AddRow(Text(direct.Contains(node.Id) ? "direct" : "transitive"), Text(node.Kind), Text(node.Id), Text(node.Version));
                console.Write(nodes);
                foreach (var declaration in graph.UnresolvedDeclarations) Note(console, "Unresolved declaration: " + declaration);
                break;
            case PackageReport packages:
                Evidence(console, packages.SourcePath, packages.Target);
                var inventory = Table("Origin", "Package", "Resolved version");
                foreach (var package in packages.Packages)
                    inventory.AddRow(Text(package.Direct ? "direct" : "transitive"), Text(package.Id), Text(package.Version));
                console.Write(inventory);
                console.MarkupLine($"[dim]{packages.Packages.Count} resolved packages · exact selected target[/]");
                if (packages.UnresolvedDependencies.Count != 0)
                    Note(console, "Unresolved constraints: " + string.Join(", ", packages.UnresolvedDependencies));
                break;
            case WhyReport why:
                Evidence(console, why.SourcePath, why.Target);
                if (why.Trace.Found) Path(console, why.Trace.Path);
                else Note(console, $"No path to {why.Trace.Dependency} in the selected target.");
                break;
            case PolicyReport policy:
                Evidence(console, policy.SourcePath, policy.Target);
                console.Write(new Panel(Text(policy.Passed ? "PASS · no forbidden dependencies" : $"FAIL · {policy.Violations.Count} forbidden dependencies"))
                    .BorderColor(policy.Passed ? Color.Green : Color.Red));
                foreach (var violation in policy.Violations)
                {
                    console.Write(Text($"Rule: {violation.Pattern}"));
                    console.WriteLine();
                    Path(console, violation.Path);
                }
                if (policy.UnresolvedDependencies.Count != 0)
                    Note(console, "Unresolved constraints outside these rules: " + string.Join(", ", policy.UnresolvedDependencies));
                break;
            case AssemblyReport assembly:
                Evidence(console, assembly.SourcePath, "static assembly metadata");
                var identity = Table("Property", "Recorded value");
                identity.AddRow("Assembly", Markup.Escape(assembly.Identity.Name));
                identity.AddRow("Identity version", Markup.Escape(assembly.Identity.Version));
                identity.AddRow("Target framework", Markup.Escape(assembly.TargetFramework ?? "not recorded"));
                identity.AddRow("File version", Markup.Escape(assembly.FileVersion ?? "not recorded"));
                identity.AddRow("Informational version", Markup.Escape(assembly.InformationalVersion ?? "not recorded"));
                identity.AddRow("MVID", assembly.ModuleVersionId);
                identity.AddRow("Reference assembly", assembly.IsReferenceAssembly ? "yes" : "no");
                console.Write(identity);
                if (assembly.AssemblyMetadata.Count != 0)
                {
                    var metadata = Table("AssemblyMetadata key", "Value");
                    foreach (var item in assembly.AssemblyMetadata) metadata.AddRow(Text(item.Key), Text(item.Value ?? "null"));
                    console.Write(metadata);
                }
                if (assembly.DebugDirectoryKinds.Count != 0)
                    Note(console, "Debug directory: " + string.Join(", ", assembly.DebugDirectoryKinds));
                var references = Table("Static reference", "Assembly version");
                foreach (var reference in assembly.References) references.AddRow(Text(reference.Name), Text(reference.Version));
                console.Write(references);
                foreach (var type in assembly.Types)
                {
                    var tree = new Tree(Text(type.Name));
                    foreach (var member in type.Members)
                        tree.AddNode(Text($"{(member.IsStatic ? "static " : "")}{member.Kind} {member.Name}"));
                    console.Write(tree);
                }
                if (assembly.ForwardedTypes.Count != 0)
                {
                    var forwarded = new Tree(Text($"Forwarded types ({assembly.ForwardedTypes.Count})"));
                    foreach (var name in assembly.ForwardedTypes.Take(40)) forwarded.AddNode(Text(name));
                    console.Write(forwarded);
                }
                break;
            case ProcessReport processes:
                var processTable = Table("PID", "Process", "Discovery");
                foreach (var process in processes.Processes)
                    processTable.AddRow(Text(process.ProcessId.ToString(CultureInfo.InvariantCulture)), Text(process.Name), Text(".NET diagnostic port"));
                console.Write(processTable);
                Note(console, "Choose a PID with: ref runtime --pid <PID>");
                break;
            case RuntimeReport runtime:
                Evidence(console, $"PID {runtime.ProcessId} · {runtime.ProcessName}", runtime.Mechanism);
                console.Write(new Panel(Text(runtime.IsConclusive ? "Completed rundown · no reported event loss" : "INCONCLUSIVE · incomplete rundown or event loss"))
                    .BorderColor(runtime.IsConclusive ? Color.Green : Color.Yellow));
                console.MarkupLine($"[dim]{runtime.StartedAtUtc:O} → {runtime.FinishedAtUtc:O} · observer PID {runtime.ObserverProcessId}[/]");
                var observed = Table("Observed assembly", "Version", "Evidence", "Modules");
                foreach (var item in runtime.Assemblies)
                {
                    var evidence = item.SeenInRundown ? "rundown" : "load event";
                    if (item.UnloadObserved) evidence += ", unload observed";
                    var parts = item.FullName.Split(',');
                    var version = parts.Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("Version=", StringComparison.Ordinal));
                    observed.AddRow(Text(parts[0]), Text(version?["Version=".Length..] ?? "not recorded"),
                        Text(evidence), Text(item.Modules.Count.ToString(CultureInfo.InvariantCulture)));
                }
                console.Write(observed);
                if (modules)
                    foreach (var item in runtime.Assemblies.Where(a => a.Modules.Count != 0))
                    {
                        var tree = new Tree(Text(item.FullName));
                        foreach (var module in item.Modules)
                        {
                            tree.AddNode(Text("IL: " + (string.IsNullOrEmpty(module.IlPath) ? "not recorded" : module.IlPath)));
                            if (!string.IsNullOrEmpty(module.NativePath)) tree.AddNode(Text("Native: " + module.NativePath));
                        }
                        console.Write(tree);
                    }
                foreach (var note in runtime.Notes) Note(console, note);
                break;
            case SdkReport sdk:
                Evidence(console, sdk.SdkDirectory, "SDK files · not Rider configuration");
                var sdkTable = Table("Evidence", "Value");
                sdkTable.AddRow("SDK directory version", Markup.Escape(sdk.DirectoryVersion));
                sdkTable.AddRow("Native CLI payload", sdk.NativeCliPayloadPresent ? "present" : "not found");
                sdkTable.AddRow("Roslyn assembly version", Markup.Escape(sdk.RoslynAssemblyVersion ?? "not found"));
                sdkTable.AddRow("Roslyn informational version", Markup.Escape(sdk.RoslynInformationalVersion ?? "not found"));
                console.Write(sdkTable);
                foreach (var payload in sdk.NativeCliPayloads) { console.Write(Text(payload)); console.WriteLine(); }
                foreach (var note in sdk.Notes) Note(console, note);
                break;
            case ReflectionReport reflection:
                Reflection(console, reflection);
                break;
            case AotInspectionReport assessment:
                Evidence(console, assessment.SourcePath, "static AOT evidence");
                console.Write(new Panel(Text("Compatibility not established · publish validation required")).BorderColor(Color.Yellow));
                var declarations = Table("Recorded evidence", "Value");
                declarations.AddRow("Code format", assessment.ManagedCodeFormat);
                declarations.AddRow("IsAotCompatible declaration", assessment.DeclaredAotCompatible?.ToString() ?? "not recorded");
                declarations.AddRow("IsTrimmable declaration", assessment.DeclaredTrimmable?.ToString() ?? "not recorded");
                console.Write(declarations);
                var annotations = Table("Owner", "Compatibility annotation");
                foreach (var annotation in assessment.Annotations.Take(40))
                    annotations.AddRow(Text(annotation.Owner), Text(annotation.Attribute));
                if (assessment.Annotations.Count != 0) console.Write(annotations);
                Reflection(console, assessment.Reflection);
                break;
            case AotPublishReport audit:
                Evidence(console, audit.ProjectPath, "Native AOT publish · " + audit.RuntimeIdentifier);
                console.Write(new Panel(Text($"Publish {audit.Outcome} · exit code {audit.ExitCode}"))
                    .BorderColor(audit.Outcome == "succeeded" ? Color.Green : Color.Yellow));
                Note(console, $"SDK {audit.SdkVersion} · {audit.Configuration} · environment {audit.EnvironmentMode}");
                if (audit.Evaluation.Succeeded)
                {
                    var properties = Table(audit.Evaluation.Phase, "Value");
                    foreach (var property in audit.Evaluation.Properties)
                        properties.AddRow(Text(property.Key), Text(string.IsNullOrEmpty(property.Value) ? "(empty)" : property.Value));
                    console.Write(properties);
                }
                else Note(console, "Property evaluation unavailable: " + audit.Evaluation.Message);
                var diagnostics = Table("Level", "Code", "Compiler diagnostic");
                foreach (var diagnostic in audit.Diagnostics.Take(40))
                    diagnostics.AddRow(Text(diagnostic.Severity), Text(diagnostic.Code), Text(diagnostic.Text));
                if (audit.Diagnostics.Count != 0) console.Write(diagnostics);
                Note(console, $"Full diagnostics: {audit.Diagnostics.Count}; log: {audit.LogPath}");
                if (audit.BinlogPath is not null) Note(console, "MSBuild binary log: " + audit.BinlogPath);
                foreach (var limit in audit.Limitations) Note(console, limit);
                break;
            case ErrorReport error:
                console.Write(new Panel(Text(error.Message)).Header("[red]Unable to conclude[/]").BorderColor(Color.Red));
                break;
        }
        console.WriteLine();
    }

    private static IAnsiConsole Create(TextWriter writer)
    {
        var terminal = ReferenceEquals(writer, Console.Out) && !Console.IsOutputRedirected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = terminal ? AnsiSupport.Detect : AnsiSupport.No,
            ColorSystem = terminal ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No
        });
        if (!terminal) console.Profile.Width = 110;
        return console;
    }

    private static Table Table(params string[] columns)
    {
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var column in columns) table.AddColumn(new TableColumn(new Markup($"[bold]{column}[/]")));
        return table;
    }

    private static Text Text(string text) => new(text);

    private static void Evidence(IAnsiConsole console, string source, string target)
    {
        console.Write(new Text(target, new Style(foreground: Color.Cyan)));
        console.WriteLine();
        var width = Math.Max(30, console.Profile.Width - 2);
        var displayed = source.Length <= width ? source : source[..20] + "…" + source[^(width - 21)..];
        console.Write(new Text(displayed, new Style(decoration: Decoration.Dim)));
        console.WriteLine();
    }

    private static void Note(IAnsiConsole console, string text)
    {
        console.Write(new Text(text, new Style(foreground: Color.Grey)));
        console.WriteLine();
    }

    private static void Path(IAnsiConsole console, IReadOnlyList<DependencyNode> path)
    {
        if (path.Count == 0) { Note(console, "No recorded path."); return; }
        static string Label(DependencyNode node) => node.Kind == "root" ? node.ProjectPath ?? node.Id : $"{node.Id}/{node.Version}";
        var tree = new Tree(Text(Label(path[0])));
        TreeNode? current = null;
        foreach (var node in path.Skip(1))
            current = current is null ? tree.AddNode(Text(Label(node))) : current.AddNode(Text(Label(node)));
        console.Write(tree);
    }

    private static void Reflection(IAnsiConsole console, ReflectionReport report)
    {
        Evidence(console, report.SourcePath, $"Static IL · {report.MethodBodiesScanned} method bodies · {report.Findings.Count} review points");
        var findings = Table("Caller / IL", "Target", "Category");
        foreach (var finding in report.Findings.Take(40))
            findings.AddRow(Text($"{finding.Caller}\nIL_{finding.IlOffset:x4}"),
                Text($"{finding.TargetType}.{finding.TargetMember}"), Text(finding.Category));
        if (report.Findings.Count != 0) console.Write(findings);
        if (report.Findings.Count > 40) Note(console, "Showing the first 40 findings; use --json for all findings.");
        foreach (var limit in report.Limitations) Note(console, limit);
    }
}
