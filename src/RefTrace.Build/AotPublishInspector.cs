using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using RefTrace.Core;

namespace RefTrace.Build;

public sealed record PublishDiagnostic(string Severity, string Code, string Text);
public sealed record BuildEvaluationReport(string Phase, bool Succeeded,
    IReadOnlyDictionary<string, string> Properties, string? Message);
public sealed record AotPublishReport(string ProjectPath, string RuntimeIdentifier, string Configuration,
    string WorkingDirectory, string Executable, IReadOnlyList<string> Arguments, string OutputDirectory,
    string LogPath, int? ExitCode, string Outcome, IReadOnlyList<PublishDiagnostic> Diagnostics,
    IReadOnlyList<string> Limitations, string SdkVersion, DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc,
    BuildEvaluationReport Evaluation, string? BinlogPath, string EnvironmentMode);

public static partial class AotPublishInspector
{
    public static async Task<AotPublishReport> RunAsync(string projectPath, string rid, string outputDirectory,
        TimeSpan timeout, string dotnet = "dotnet", CancellationToken cancellationToken = default,
        string? targetFramework = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var project = Path.GetFullPath(projectPath);
        var requestedOutput = Path.GetFullPath(outputDirectory);
        if (!File.Exists(project) || !project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new InspectionException("Specify an existing .csproj for the opt-in Native AOT publish audit.");
        if (string.IsNullOrWhiteSpace(rid)) throw new InspectionException("Specify a runtime identifier with --rid.");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(30))
            throw new InspectionException("Publish timeout must be greater than zero and at most 1800 seconds.");
        var workingDirectory = Path.GetDirectoryName(project)!;
        if (string.Equals(requestedOutput, workingDirectory, StringComparison.Ordinal))
            throw new InspectionException("Use a separate output directory, not the project's source directory.");
        var started = DateTimeOffset.UtcNow;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var sdkVersion = await SdkVersionAsync(dotnet, workingDirectory, deadline.Token).ConfigureAwait(false);
        var output = Path.Combine(requestedOutput, $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        var artifacts = Path.Combine(output, "build-artifacts");
        var evaluation = await EvaluateAsync(dotnet, project, workingDirectory, rid, artifacts, targetFramework, deadline.Token).ConfigureAwait(false);
        var binlog = Path.Combine(output, $"aot-publish-{Guid.NewGuid():N}.binlog");
        var arguments = new List<string> { "publish", project, "-c", "Release", "-r", rid,
            "-p:PublishAot=true", "-p:TreatWarningsAsErrors=true", "-p:TrimmerSingleWarn=false",
            "--artifacts-path", artifacts, "-o", Path.Combine(output, "native"), "--nologo",
            $"-bl:{binlog};ProjectImports=None" };
        if (!string.IsNullOrWhiteSpace(targetFramework)) arguments.AddRange(["--framework", targetFramework]);
        var start = new ProcessStartInfo(dotnet, arguments)
        {
            WorkingDirectory = workingDirectory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        using var process = Process.Start(start) ?? throw new InspectionException("The dotnet process could not be started.");
        var capture = process.ReadAllTextAsync(cancellationToken);
        string outcome;
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            outcome = process.ExitCode == 0 ? "succeeded" : "failed";
        }
        catch (OperationCanceledException)
        {
            outcome = cancellationToken.IsCancellationRequested ? "cancelled" : "timed-out";
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The owned process exited during cancellation. */ }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        string text;
        try
        {
            var (stdout, stderr) = await capture.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            text = stdout + Environment.NewLine + stderr;
        }
        catch (OperationCanceledException) { text = "Output capture was cancelled."; }
        catch (TimeoutException) { text = "Output capture did not complete."; outcome = "incomplete-output"; }
        var log = Path.Combine(output, $"aot-publish-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(log, text, CancellationToken.None).ConfigureAwait(false);
        var diagnostics = ParseDiagnostics(text);
        if (outcome == "succeeded" && diagnostics.Any(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
            outcome = "failed";
        return new(project, rid, "Release", workingDirectory, dotnet, arguments, output, log,
            process.ExitCode, outcome, diagnostics,
            ["This command executed the project's restore, build and publish targets.",
             "Success applies to this specific invocation, not every RID, SDK or runtime input.",
             "Project-supplied warning suppressions and build customizations remain in effect.",
             "Recorded properties come from evaluation before publish; targets may change values later."],
            sdkVersion, started, DateTimeOffset.UtcNow, evaluation,
            File.Exists(binlog) && new FileInfo(binlog).Length != 0 ? binlog : null, "inherited");
    }

    private static async Task<string> SdkVersionAsync(string dotnet, string directory, CancellationToken token)
    {
        var result = await QueryAsync(dotnet, directory, ["--version"], token).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            throw new InspectionException("Cannot determine the selected SDK: " + result.Error.Trim());
        return result.Output.Trim();
    }

    private static async Task<BuildEvaluationReport> EvaluateAsync(string dotnet, string project, string directory,
        string rid, string artifacts, string? targetFramework, CancellationToken token)
    {
        var arguments = new List<string> { "msbuild", project, "-nologo", "-verbosity:quiet",
             "-getProperty:TargetFramework,TargetFrameworks,RuntimeIdentifier,PublishAot,PublishTrimmed,SelfContained,IsAotCompatible,IsTrimmable,NETCoreSdkVersion,RuntimeFrameworkVersion,ArtifactsPath,NoWarn,WarningsAsErrors,TreatWarningsAsErrors",
             "-p:Configuration=Release", $"-p:RuntimeIdentifier={rid}", "-p:PublishAot=true",
             "-p:TreatWarningsAsErrors=true", $"-p:ArtifactsPath={artifacts}" };
        if (!string.IsNullOrWhiteSpace(targetFramework)) arguments.Add($"-p:TargetFramework={targetFramework}");
        var result = await QueryAsync(dotnet, directory, arguments, token).ConfigureAwait(false);
        return ParseEvaluation(result.Output, result.ExitCode, result.Error);
    }

    public static BuildEvaluationReport ParseEvaluation(string output, int exitCode, string error = "")
    {
        const string phase = "MSBuild evaluation before publish";
        if (exitCode != 0) return new(phase, false, new Dictionary<string, string>(), error.Trim());
        try
        {
            using var json = JsonDocument.Parse(output);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("Properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                return new(phase, false, new Dictionary<string, string>(), "MSBuild did not return a Properties object.");
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return new(phase, false, result, "MSBuild returned a non-string property value.");
                result.Add(property.Name, property.Value.GetString() ?? "");
            }
            return new(phase, true, result, null);
        }
        catch (JsonException) { return new(phase, false, new Dictionary<string, string>(), "MSBuild evaluation output was not valid JSON."); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> QueryAsync(string dotnet, string directory,
        IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(dotnet, arguments)
        {
            WorkingDirectory = directory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        using var process = Process.Start(start) ?? throw new InspectionException("Cannot determine the selected SDK.");
        var capture = process.ReadAllTextAsync(token);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await capture.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
        var (output, error) = await capture.ConfigureAwait(false);
        return (process.ExitCode, output, error);
    }

    public static IReadOnlyList<PublishDiagnostic> ParseDiagnostics(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => (Line: line, Match: DiagnosticPattern().Match(line)))
            .Where(pair => pair.Match.Success)
            .Select(pair => new PublishDiagnostic(pair.Match.Groups["severity"].Value,
                pair.Match.Groups["code"].Value, pair.Line.Trim()))
            .Distinct().ToArray();

    [GeneratedRegex(@"\b(?<severity>warning|error)\s+(?<code>IL\d{4}|CS\d{4}|MSB\d{4}|NETSDK\d+|NU\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPattern();
}
