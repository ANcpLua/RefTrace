using System.CommandLine;
using System.Text.Json;
using RefTrace.Core;
using RefTrace.Runtime;
using RefTrace.Metadata;
using RefTrace.Build;

namespace RefTrace.Cli;

public static partial class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var root = new RootCommand("Trace .NET dependencies and assemblies.") { HelpName = "ref" };
        var json = new Option<bool>("--json") { Description = "Write JSON.", Recursive = true };
        root.Options.Add(json);
        var plain = new Option<bool>("--plain") { Description = "Write plain text.", Recursive = true };
        root.Options.Add(plain);

        var versions = new Command("versions", "Compare NuGet package versions.");
        var indexFile = new Argument<string>("index") { Description = "Local NuGet flat-container index JSON." };
        var currentVersion = new Option<string>("--current") { Required = true, Description = "Currently selected package version." };
        versions.Arguments.Add(indexFile);
        versions.Options.Add(currentVersion);
        versions.SetAction(parse => Execute(parse.GetValue(json), "versions", () =>
        {
            var report = ReleaseCatalog.Read(parse.GetValue(indexFile)!, parse.GetValue(currentVersion)!);
            return (report, 0, $"Current: {report.Current}\nStable: {report.LatestStable}\nPreview: {report.LatestPrerelease}\nSelected: {report.Selected} ({report.Selection})");
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(versions);

        var (assets, assetsFile, assetsTarget) = GraphCommand("assets", "Show the dependency graph.");
        assets.SetAction((parse, token) => ExecuteAsync(parse.GetValue(json), "assets", async () =>
        {
            var graph = await ReadGraphAsync(parse.GetValue(assetsFile)!, parse.GetValue(assetsTarget), token).ConfigureAwait(false);
            return (graph, 0, RenderGraph(graph));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(assets);

        var (packagesCommand, packagesFile, packagesTarget) = GraphCommand("packages", "List resolved packages.");
        var includeAssets = new Option<bool>("--assets") { Description = "Include compile, analyzer and runtime asset paths." };
        var packagePattern = new Option<string>("--package") { Description = "Filter package IDs by glob (case-insensitive)." };
        var directOnly = new Option<bool>("--direct") { Description = "Only list direct package dependencies." };
        packagesCommand.Options.Add(includeAssets);
        packagesCommand.Options.Add(packagePattern);
        packagesCommand.Options.Add(directOnly);
        packagesCommand.SetAction((parse, token) => ExecuteAsync(parse.GetValue(json), "packages", async () =>
        {
            var graph = await ReadGraphAsync(parse.GetValue(packagesFile)!, parse.GetValue(packagesTarget), token).ConfigureAwait(false);
            var direct = graph.Edges.Where(e => e.From == graph.RootId).Select(e => e.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var assetPaths = parse.GetValue(includeAssets);
            var report = new PackageReport(graph.SourcePath, graph.Target,
                graph.Nodes.Where(n => n.Kind == "package"
                    && (!parse.GetValue(directOnly) || direct.Contains(n.Id))
                    && (parse.GetValue(packagePattern) is not { } pattern || System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, n.Id, ignoreCase: true)))
                    .Select(n => new PackageItem(n.Id, n.Version, direct.Contains(n.Id),
                        assetPaths ? n.CompileAssets : null, assetPaths ? n.RuntimeAssets : null,
                        assetPaths ? n.AnalyzerAssets : null)).ToArray(),
                graph.Nodes.Where(n => n.Kind == "unresolved").Select(n => n.Id).ToArray(), graph.AnalyzerAssetsRecorded);
            IEnumerable<string> lines = report.Packages.SelectMany(p =>
                new[] { $"{p.Id}/{p.Version} ({(p.Direct ? "direct" : "transitive")})" }
                    .Concat((p.CompileAssets ?? []).Select(a => "  compile: " + a))
                    .Concat((p.AnalyzerAssets ?? []).Select(a => "  analyzer: " + a))
                    .Concat((p.RuntimeAssets ?? []).Select(a => "  runtime: " + a)));
            if (assetPaths && !report.AnalyzerAssetsRecorded) lines = lines.Append(AnalyzerAssetsNotRecorded);
            return (report, 0, string.Join(Environment.NewLine, lines));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(packagesCommand);

        var dependency = new Argument<string>("dependency") { Description = "Exact package or project library ID (case-insensitive)." };
        var (why, whyFile, whyTarget) = GraphCommand("why", "Explain a dependency.", dependency);
        why.SetAction((parse, token) => ExecuteAsync(parse.GetValue(json), "why", async () =>
        {
            var graph = await ReadGraphAsync(parse.GetValue(whyFile)!, parse.GetValue(whyTarget), token).ConfigureAwait(false);
            var trace = GraphQueries.Explain(graph, parse.GetValue(dependency)!);
            return (new WhyReport(graph.SourcePath, graph.Target, trace), trace.Found ? 0 : 1,
                trace.Found ? RenderPath(trace.Path) : $"No dependency path to '{trace.Dependency}' in {graph.Target}.");
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(why);

        var (check, checkFile, checkTarget) = GraphCommand("check", "Check forbidden dependencies.");
        var packages = new Option<string[]>("--forbid-package")
        {
            Description = "Forbidden package ID glob; repeat for multiple patterns. Supports * and ?.",
            Arity = ArgumentArity.OneOrMore
        };
        var projects = new Option<string[]>("--forbid-project")
        {
            Description = "Forbidden project library ID or recorded project path glob; repeat for multiple patterns.",
            Arity = ArgumentArity.OneOrMore
        };
        check.Options.Add(packages);
        check.Options.Add(projects);
        check.SetAction((parse, token) => ExecuteAsync(parse.GetValue(json), "check", async () =>
        {
            var graph = await ReadGraphAsync(parse.GetValue(checkFile)!, parse.GetValue(checkTarget), token).ConfigureAwait(false);
            var report = GraphQueries.Check(graph, parse.GetValue(packages) ?? [], parse.GetValue(projects) ?? []);
            return (report, report.Passed ? 0 : 1, report.Passed
                ? $"PASS: no forbidden dependencies in {report.Target}."
                : string.Join(Environment.NewLine, report.Violations.Select(v =>
                    $"FAIL: {v.Dependency.Kind} {v.Dependency.Id}/{v.Dependency.Version} matches '{v.Pattern}'.{Environment.NewLine}  {RenderPath(v.Path)}")));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(check);

        var assembly = new Command("assembly", "Inspect a DLL or EXE.");
        var assemblyFile = new Argument<string>("file") { Description = "Path to a managed DLL or EXE." };
        var type = new Option<string>("--type") { Description = "Public type metadata-name glob; use + for nested types and `N for generic arity." };
        assembly.Arguments.Add(assemblyFile);
        assembly.Options.Add(type);
        assembly.SetAction(parse => Execute(parse.GetValue(json), "assembly", () =>
        {
            var report = AssemblyInspector.Read(parse.GetValue(assemblyFile)!, parse.GetValue(type));
            return (report, 0, RenderAssembly(report));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(assembly);

        var reflection = new Command("reflection", "Find reflection and dynamic-code calls.");
        var reflectionFile = new Argument<string>("file") { Description = "Managed implementation DLL or EXE." };
        var failOnFindings = new Option<bool>("--fail-on-findings") { Description = "Return exit 1 when catalogued method references exist; this is a policy, not an AOT verdict." };
        reflection.Arguments.Add(reflectionFile);
        reflection.Options.Add(failOnFindings);
        reflection.SetAction(parse => Execute(parse.GetValue(json), "reflection", () =>
        {
            var report = ReflectionInspector.Read(parse.GetValue(reflectionFile)!);
            return (report, parse.GetValue(failOnFindings) && report.Findings.Count != 0 ? 1 : 0,
                $"Static method references: {report.Findings.Count}; method bodies scanned: {report.MethodBodiesScanned}.{Environment.NewLine}" +
                string.Join(Environment.NewLine, report.Findings.Select(f => $"{f.Caller} IL_{f.IlOffset:x4} -> {f.TargetType}.{f.TargetMember} ({f.Category})"
                    + (f.DeclaredMethod is null ? "" : $"; declared method {f.DeclaredMethod}"))));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(reflection);

        var aot = new Command("aot", "Inspect or try Native AOT.");
        var aotInspect = new Command("inspect", "Read compatibility declarations, code format and static reflection evidence. Does not establish AOT compatibility.");
        var aotFile = new Argument<string>("file") { Description = "Managed implementation DLL or EXE." };
        aotInspect.Arguments.Add(aotFile);
        aotInspect.SetAction(parse => Execute(parse.GetValue(json), "aot-inspect", () =>
        {
            var report = AotInspector.Read(parse.GetValue(aotFile)!);
            return (report, 0, $"Assessment: {report.Assessment}{Environment.NewLine}Code format: {report.ManagedCodeFormat}{Environment.NewLine}" +
                $"Declared AOT compatible: {report.DeclaredAotCompatible?.ToString() ?? "not recorded"}{Environment.NewLine}" +
                $"Reflection review points: {report.Reflection.Findings.Count}");
        }, output, error, parse.GetValue(plain)));
        aot.Subcommands.Add(aotInspect);

        var publish = new Command("publish", "Execute dotnet publish with Native AOT enabled. Runs build targets and writes output; requires explicit output directory.");
        var publishProject = new Argument<string>("project") { Description = "Existing .csproj; its directory determines SDK/global.json selection." };
        var publishRid = new Option<string>("--rid") { Required = true, Description = "Runtime identifier, for example osx-arm64." };
        var publishOutput = new Option<string>("--output") { Required = true, Description = "Dedicated directory for native output, intermediate artifacts and the log." };
        var publishFramework = new Option<string>("--framework", "-f") { Description = "Explicit target framework for a multi-targeted project." };
        var publishTimeout = new Option<double>("--timeout") { DefaultValueFactory = _ => 300, Description = "Timeout in seconds (maximum 1800)." };
        var dotnet = new Option<string>("--dotnet") { DefaultValueFactory = _ => "dotnet", Description = "dotnet executable; arguments are passed without a shell." };
        publish.Arguments.Add(publishProject);
        publish.Options.Add(publishRid);
        publish.Options.Add(publishOutput);
        publish.Options.Add(publishFramework);
        publish.Options.Add(publishTimeout);
        publish.Options.Add(dotnet);
        publish.SetAction(async (parse, token) =>
        {
            try
            {
                var report = await AotPublishInspector.RunAsync(parse.GetValue(publishProject)!, parse.GetValue(publishRid)!,
                    parse.GetValue(publishOutput)!, TimeSpan.FromSeconds(parse.GetValue(publishTimeout)),
                    parse.GetValue(dotnet)!, token, parse.GetValue(publishFramework)).ConfigureAwait(false);
                if (parse.GetValue(json)) WriteJson(output, "aot-publish", report);
                else if (parse.GetValue(plain)) await output.WriteLineAsync($"Publish: {report.Outcome}; exit code: {report.ExitCode}; log: {report.LogPath}".AsMemory(), token).ConfigureAwait(false);
                else TerminalRenderer.Render("aot-publish", report, output);
                return report.Outcome switch { "succeeded" => 0, "failed" => 1, _ => 2 };
            }
            catch (OperationCanceledException)
            {
                return Error("Publish audit cancelled before completion.", parse.GetValue(json), output, error, parse.GetValue(plain), "cancelled");
            }
            catch (Exception ex) when (IsInputError(ex))
            {
                return Error(ex, parse.GetValue(json), output, error, parse.GetValue(plain));
            }
        });
        aot.Subcommands.Add(publish);
        root.Subcommands.Add(aot);

        var sdk = new Command("sdk", "Inspect an installed SDK.");
        var sdkDirectory = new Argument<string>("input") { Description = "Project, working directory, or explicit SDK directory (default: current directory).", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => "." };
        sdk.Arguments.Add(sdkDirectory);
        sdk.SetAction((parse, token) => ExecuteAsync(parse.GetValue(json), "sdk", async () =>
        {
            var report = SdkInspector.Read(await ProjectInputs.ResolveSdkAsync(parse.GetValue(sdkDirectory)!, token).ConfigureAwait(false));
            return (report, 0, $"SDK: {report.SdkDirectory}{Environment.NewLine}Native CLI payload: {report.NativeCliPayloadPresent}{Environment.NewLine}Roslyn: {report.RoslynInformationalVersion}");
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(sdk);

        var processes = new Command("processes", "List .NET processes.");
        processes.SetAction(parse => Execute(parse.GetValue(json), "processes", () =>
        {
            var report = new ProcessReport(RuntimeInspector.Processes());
            return (report, 0, string.Join(Environment.NewLine, report.Processes.Select(p => $"{p.ProcessId}\t{p.Name}")));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(processes);

        var runtime = new Command("runtime", "Observe loaded assemblies.");
        var pid = new Option<int>("--pid") { Required = true, Description = "Target process ID, from ref processes." };
        var seconds = new Option<double>("--seconds") { DefaultValueFactory = _ => 1, Description = "Observation window, 0.1–30 seconds." };
        var timeout = new Option<double>("--timeout") { DefaultValueFactory = _ => 30, Description = "Total timeout including rundown, in seconds (maximum 300)." };
        var modules = new Option<bool>("--modules") { Description = "Show recorded IL/native module paths in terminal output." };
        runtime.Options.Add(pid);
        runtime.Options.Add(seconds);
        runtime.Options.Add(timeout);
        runtime.Options.Add(modules);
        runtime.SetAction(async (parse, token) =>
        {
            try
            {
                var report = await RuntimeInspector.CaptureAsync(parse.GetValue(pid),
                    TimeSpan.FromSeconds(parse.GetValue(seconds)), TimeSpan.FromSeconds(parse.GetValue(timeout)), token).ConfigureAwait(false);
                if (parse.GetValue(json)) WriteJson(output, "runtime", report);
                else if (parse.GetValue(plain))
                    await output.WriteLineAsync(($"PID: {report.ProcessId}{Environment.NewLine}Rundown complete: {report.RundownCompleted}{Environment.NewLine}Events lost: {report.EventsLost}{Environment.NewLine}" +
                        string.Join(Environment.NewLine, report.Assemblies.Select(a => a.FullName))).AsMemory(), token).ConfigureAwait(false);
                else TerminalRenderer.Render("runtime", report, output, parse.GetValue(modules));
                return report.IsConclusive ? 0 : 2;
            }
            catch (OperationCanceledException)
            {
                return Error("Runtime observation cancelled.", parse.GetValue(json), output, error, parse.GetValue(plain), "cancelled");
            }
            catch (Exception ex) when (IsInputError(ex))
            {
                return Error(ex, parse.GetValue(json), output, error, parse.GetValue(plain));
            }
        });
        root.Subcommands.Add(runtime);

        AddReviewCommand(root, json, plain, output, error);
        CliHelp.Configure(root, output, error);
        var parsed = root.Parse(args.Length == 0 ? ["--help"] : args);
        if (parsed.Errors.Count != 0)
            return Error(string.Join(Environment.NewLine, parsed.Errors.Select(e => e.Message)),
                parsed.GetValue(json), output, error, parsed.GetValue(plain));
        if (parsed.Action?.ClearsParseErrors != true && parsed.GetValue(json) && parsed.GetValue(plain))
            return Error("Choose either --json or --plain.", true, output, error);
        return await parsed.InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false
        }, cancellationToken).ConfigureAwait(false);
    }

    internal const string AnalyzerAssetsNotRecorded =
        "Analyzer assets: not recorded. The restore did not set RestoreEnableAnalyzerAssets.";

    private static (Command Command, Argument<string> File, Option<string> Target) GraphCommand(string name, string description, Argument? leading = null)
    {
        var command = new Command(name, description);
        var file = new Argument<string>("input") { Description = "Project, single-project .slnx solution, directory, or restored assets file (default: current directory).", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => "." };
        var target = new Option<string>("--framework", "-f") { Description = "Exact target key or TFM[/RID]. Required when the file has multiple targets." };
        if (leading is not null) command.Arguments.Add(leading);
        command.Arguments.Add(file);
        command.Options.Add(target);
        return (command, file, target);
    }

    private static async Task<DependencyGraph> ReadGraphAsync(string input, string? target, CancellationToken token)
    {
        var resolved = await ProjectInputs.ResolveAsync(input, token).ConfigureAwait(false);
        return AssetsInspector.Read(resolved.AssetsPath, target);
    }

    private static async Task<int> ExecuteAsync<T>(bool json, string kind, Func<Task<(T Data, int ExitCode, string Text)>> operation,
        TextWriter output, TextWriter error, bool plain = false) where T : notnull
    {
        try
        {
            var result = await operation().ConfigureAwait(false);
            if (json) WriteJson(output, kind, result.Data);
            else if (plain) await output.WriteLineAsync(result.Text).ConfigureAwait(false);
            else TerminalRenderer.Render(kind, result.Data, output);
            return result.ExitCode;
        }
        catch (OperationCanceledException ex) { return Error(ex, json, output, error, plain); }
        catch (Exception ex) when (IsInputError(ex)) { return Error(ex, json, output, error, plain); }
    }

    private static int Error(Exception exception, bool json, TextWriter output, TextWriter error, bool plain = false)
    {
        var code = exception switch
        {
            InspectionException inspection => inspection.Code,
            OperationCanceledException => "cancelled",
            FileNotFoundException => "file-not-found",
            DirectoryNotFoundException => "directory-not-found",
            UnauthorizedAccessException => "access-denied",
            BadImageFormatException or JsonException => "invalid-artifact",
            ArgumentException => "invalid-argument",
            System.ComponentModel.Win32Exception => "tool-unavailable",
            _ => "io-error"
        };
        return Error(exception.Message, json, output, error, plain, code,
            exception is InspectionException specific ? specific.Input : (exception as FileNotFoundException)?.FileName,
            (exception as InspectionException)?.Candidates);
    }

    private static int Execute<T>(bool json, string kind, Func<(T Data, int ExitCode, string Text)> operation,
        TextWriter output, TextWriter error, bool plain = false) where T : notnull
    {
        try
        {
            var result = operation();
            if (json) WriteJson(output, kind, result.Data);
            else if (plain) output.WriteLine(result.Text);
            else TerminalRenderer.Render(kind, result.Data, output);
            return result.ExitCode;
        }
        catch (Exception ex) when (IsInputError(ex))
        {
            return Error(ex, json, output, error, plain);
        }
    }

    private static bool IsInputError(Exception ex) => ex is InspectionException or IOException or UnauthorizedAccessException
        or BadImageFormatException or ArgumentException or JsonException or System.ComponentModel.Win32Exception;

    private static int Error(string message, bool json, TextWriter output, TextWriter error, bool plain = false, string code = "invalid-argument", string? input = null, IReadOnlyList<string>? candidates = null)
    {
        if (json) WriteJson(output, "error", new ErrorReport(code, message, input, candidates));
        else if (plain)
        {
            error.WriteLine($"error [{code}]: {message}");
            if (input is not null) error.WriteLine($"Input: {input}");
            foreach (var candidate in candidates ?? []) error.WriteLine($"  {candidate}");
        }
        else TerminalRenderer.Render("error", new ErrorReport(code, message, input, candidates), error);
        return 2;
    }

    private static void WriteJson<T>(TextWriter output, string kind, T data)
        => output.WriteLine(JsonSerializer.Serialize(new ReportEnvelope<T>(2, kind, data),
            RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<T>>()));

    private static string RenderGraph(DependencyGraph graph)
    {
        var direct = graph.Edges.Where(e => e.From == graph.RootId).Select(e => e.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> lines =
        [
            $"Project: {graph.ProjectPath}", $"Target: {graph.Target}", $"Evidence: {graph.SourcePath}",
            .. graph.Nodes.Where(n => n.Id != graph.RootId).Select(n =>
                $"  {(direct.Contains(n.Id) ? "direct" : "transitive"),-10} {n.Kind,-8} {n.Id}/{n.Version}"),
            .. graph.UnresolvedDeclarations.Select(d => $"  unresolved declaration: {d}")
        ];
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderPath(IReadOnlyList<DependencyNode> path)
        => string.Join(" -> ", path.Select(n => n.Kind == "root" ? n.ProjectPath : $"{n.Id}/{n.Version}"));

    private static string RenderAssembly(AssemblyReport report)
    {
        List<string> lines =
        [
            $"Assembly: {report.Identity.Name}, Version={report.Identity.Version}",
            $"Target framework: {report.TargetFramework ?? "not recorded"}",
            $"File version: {report.FileVersion ?? "not recorded"}",
            $"Informational version: {report.InformationalVersion ?? "not recorded"}",
            $"MVID: {report.ModuleVersionId}",
            $"Reference assembly: {report.IsReferenceAssembly}",
            $"Memory-safety rules: {report.MemorySafetyRulesVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "not recorded"}",
            $"Evidence: {report.SourcePath}",
            "Static assembly references:",
            .. report.References.Select(r => $"  {r.Name}, Version={r.Version}, PublicKeyToken={r.PublicKeyToken ?? "none"}")
        ];
        foreach (var type in report.Types)
        {
            lines.Add($"Type: {DescribeType(type)}");
            lines.AddRange(type.Members.Select(m => $"  {m.Signature}"));
            lines.AddRange(type.Notes.Select(note => $"  note: {note}"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    // Kind, modifiers and the C# 15 / .NET 11 type facts that metadata records beyond the members.
    internal static string DescribeType(ApiType type)
    {
        var text = string.Join(" ", [.. type.Modifiers, type.Kind, type.Name]);
        if (type.UnionCases is { } cases) text += "; union of " + (cases.Count == 0 ? "no public single-parameter constructors" : string.Join(", ", cases));
        if (type.ClosedDerivedTypes is { } derived) text += "; direct subtypes: " + string.Join(", ", derived);
        if (type.Layout is { } layout && (type.Kind, layout) is not (("class", "auto") or ("struct", "sequential"))) text += "; layout: " + layout;
        return text;
    }
}
