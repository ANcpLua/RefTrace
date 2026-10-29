using System.Text.Json;
using System.CommandLine;
using RefTrace.Build;
using RefTrace.Core;

namespace RefTrace.Cli;

internal sealed record ReviewInputs(string Root, IReadOnlyList<ProjectInput> Projects);
internal sealed record ReviewPackage(string Package, string Current, string? Selected, bool Direct,
    bool HasUpdate, string Status, string EvidenceDirectory, string? Error);
internal sealed record ReviewReport(string Root, string ReportPath, int PackageCount, int UpdateCount,
    int FailureCount, IReadOnlyList<ReviewPackage> Packages);

public static partial class CliApplication
{
    private static void AddReviewCommand(RootCommand root, Option<bool> json, Option<bool> plain,
        TextWriter output, TextWriter error)
    {
        var command = new Command("review", "Review restored packages, newer versions, source and API evidence.");
        var input = new Argument<string>("input") { Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => ".", Description = "Project, .slnx solution, or repository directory (default: current directory)." };
        var folder = new Option<string>("--output") { Description = "New evidence directory; defaults under the input's artifacts/package-audit/." };
        var catalog = new Option<bool>("--catalog-only") { Description = "Compare listed versions without downloading package/source/DLL evidence." };
        var offline = new Option<bool>("--offline") { Description = "Use saved evidence only; fail for missing cache entries." };
        var refresh = new Option<bool>("--refresh") { Description = "Refresh remote release metadata." };
        var package = new Option<string>("--package") { Description = "Only review package IDs matching this glob." };
        var probes = new Option<string>("--probes") { Description = "Optional JSON file selecting source paths and types to decompile." };
        command.Arguments.Add(input);
        command.Options.Add(folder);
        command.Options.Add(catalog);
        command.Options.Add(offline);
        command.Options.Add(refresh);
        command.Options.Add(package);
        command.Options.Add(probes);
        command.SetAction((parse, token) => ExecuteAsync(parse.GetValue(json), "review", async () =>
        {
            if (parse.GetValue(offline) && parse.GetValue(refresh))
                throw new InspectionException("invalid-argument", "Choose either --offline or --refresh.");
            var report = await ReviewAsync(parse.GetValue(input)!, parse.GetValue(folder), parse.GetValue(catalog),
                parse.GetValue(offline), parse.GetValue(refresh), parse.GetValue(package), parse.GetValue(probes), token).ConfigureAwait(false);
            return (report, report.FailureCount == 0 ? 0 : 1,
                $"{report.PackageCount} packages; {report.UpdateCount} newer candidates; {report.FailureCount} incomplete.\nEvidence: {report.ReportPath}\n"
                + string.Join(Environment.NewLine, report.Packages.Select(p => $"{p.Package}: {p.Current} -> {p.Selected ?? "unknown"} ({p.Status})")));
        }, output, error, parse.GetValue(plain)));
        root.Subcommands.Add(command);
    }

    private static async Task<ReviewReport> ReviewAsync(string input, string? output, bool catalogOnly,
        bool offline, bool refresh, string? package, string? probes, CancellationToken token)
    {
        var path = Path.GetFullPath(input);
        var root = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        var projects = ProjectInputs.FindProjects(path);
        if (projects.Count == 0) throw new InspectionException("project-not-found", "No .NET projects were found to review.", path);
        var inputs = new List<ProjectInput>();
        foreach (var project in projects)
            inputs.Add(await ProjectInputs.EvaluateAsync(project, token).ConfigureAwait(false));
        var folder = output is null ? Path.Combine(root, "artifacts", "package-audit", "review-" + Guid.NewGuid().ToString("N")) : Path.GetFullPath(output);
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new InspectionException("output-exists", "Choose a new evidence directory for this review.", folder);
        var backend = Path.Combine(Path.GetDirectoryName(typeof(CliApplication).Assembly.Location)!, "audit");
        if (!File.Exists(Path.Combine(backend, "reftrace_audit", "__main__.py")))
            throw new InspectionException("tool-unavailable", "The installed RefTrace package is missing its review collector.", backend);
        Directory.CreateDirectory(folder);
        var manifest = Path.Combine(folder, "inputs.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new ReviewInputs(root, inputs),
            RefTraceJsonContext.Default.ReviewInputs), token).ConfigureAwait(false);
        var arguments = new List<string> { "run", "--no-project", "--no-python-downloads", "--python", "3.14", "python", "-m", "reftrace_audit",
            "--manifest", manifest, "--output", folder, "--ref-assembly", typeof(CliApplication).Assembly.Location,
            "--probes", probes is null ? Path.Combine(backend, "probes.json") : Path.GetFullPath(probes) };
        if (catalogOnly) arguments.Add("--catalog-only");
        if (offline) arguments.Add("--offline");
        if (refresh) arguments.Add("--refresh");
        if (package is not null) arguments.AddRange(["--package", package]);
        var result = await ProcessCapture.RunAsync("uv", arguments, backend, TimeSpan.FromMinutes(30), token).ConfigureAwait(false);
        var reportPath = Path.Combine(folder, "report.json");
        if (!File.Exists(reportPath) || result.ExitCode is not (0 or 1))
            throw new InspectionException("review-failed", "Review collector failed. Ensure uv, Python 3.14 and (for full reviews) ilspycmd are installed.\n" + result.Error.Trim(), folder);
        var stream = File.OpenRead(reportPath);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var packages = document.RootElement.GetProperty("packages").EnumerateArray().Select(row =>
        {
            var name = row.GetProperty("package").GetString()!;
            var current = row.GetProperty("current").GetString()!;
            var selected = row.TryGetProperty("catalog", out var release) ? release.GetProperty("selected").GetString() : null;
            return new ReviewPackage(name, current, selected, row.GetProperty("direct").GetBoolean(),
                selected is not null && release.GetProperty("selection").GetString() != "current",
                row.GetProperty("status").GetString()!, Path.Combine(folder, name, current),
                row.TryGetProperty("error", out var failure) ? failure.GetString() : null);
        }).ToArray();
        return new(root, reportPath, packages.Length,
            packages.Count(p => p.HasUpdate),
            packages.Count(p => p.Status is "failed" or "incomplete-source"), packages);
    }
}
