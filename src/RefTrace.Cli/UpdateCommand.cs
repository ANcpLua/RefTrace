using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RefTrace.Build;
using RefTrace.Core;

namespace RefTrace.Cli;

internal sealed record ToolUpdateReport(string Target, string Outcome, int? ExitCode = null,
    string? Output = null, string? Error = null, string? ResultFile = null);

public static partial class CliApplication
{
    private static void AddUpdateCommand(RootCommand root, Option<bool> json, Option<bool> plain,
        TextWriter output, TextWriter error)
    {
        var update = new Command("update", "Update this RefTrace installation to the latest stable package.");
        update.Aliases.Add("--update");
        var source = new Option<string>("--source")
        {
            Description = "NuGet feed URL or local package directory.",
            DefaultValueFactory = _ => "https://api.nuget.org/v3/index.json"
        };
        update.Options.Add(source);
        update.SetAction(async (parse, token) =>
        {
            try
            {
                // The SDK can remove the old tool store while this process is still returning its report.
                // Resolve the serializer and its assembly dependencies before starting the update.
                var jsonType = parse.GetValue(json)
                    ? RefTraceJsonContext.Default.Options.GetTypeInfo<ReportEnvelope<ToolUpdateReport>>()
                    : null;
                var report = await UpdateToolAsync(parse.GetValue(source)!, token).ConfigureAwait(false);
                // Deliver the outcome even if cancellation arrives after the update or handoff.
                if (jsonType is not null)
                    await output.WriteLineAsync(JsonSerializer.Serialize(new ReportEnvelope<ToolUpdateReport>(2, "update", report), jsonType)
                        .AsMemory(), CancellationToken.None).ConfigureAwait(false);
                else if (report.ResultFile is { } file)
                    await output.WriteLineAsync($"Update scheduled for {report.Target}. After ref exits, the SDK writes its result to {file}".AsMemory(), CancellationToken.None).ConfigureAwait(false);
                else
                {
                    await output.WriteAsync(report.Output.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                    await error.WriteAsync(report.Error.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                }
                return report.ExitCode ?? 0;
            }
            catch (OperationCanceledException ex) { return Error(ex, parse.GetValue(json), output, error, parse.GetValue(plain)); }
            catch (Exception ex) when (IsInputError(ex)) { return Error(ex, parse.GetValue(json), output, error, parse.GetValue(plain)); }
        });
        root.Subcommands.Add(update);
    }

    private static async Task<ToolUpdateReport> UpdateToolAsync(string source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var cwd = Directory.GetCurrentDirectory();
        var tools = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Parent;
        if (tools is not { Name: "tools", Parent: { Parent: { } package } installedVersion }
            || !package.Name.Equals("reftrace", StringComparison.OrdinalIgnoreCase))
            throw new InspectionException("unsupported-installation", "Run ref update from an installed RefTrace .NET tool.");

        // Global and --tool-path shims both own a .store/<id>/<version>/<id>/<version>/tools/... tree.
        var store = package.Parent?.Parent?.Parent;
        string target;
        string selector;
        if (store is { Name: ".store", Parent: { } shimDirectory }
            && package.Parent!.Name == installedVersion.Name
            && package.Parent.Parent!.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase))
        {
            target = shimDirectory.FullName;
            selector = "--tool-path";
            if (OperatingSystem.IsWindows()) return ScheduleWindowsShimUpdate(target, source, cwd);
        }
        else
        {
            // Ask the SDK which manifest is in scope. An update must never add a missing tool entry.
            var list = await ProcessCapture.RunAsync("dotnet", ["tool", "list", "--local", "--format", "json"],
                cwd, TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
            if (list.ExitCode != 0) throw new InspectionException("sdk-unavailable", list.Error + list.Output);
            using var manifestList = JsonDocument.Parse(list.Output);
            if (manifestList.RootElement.ValueKind != JsonValueKind.Object
                || !manifestList.RootElement.TryGetProperty("data", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InspectionException("sdk-unavailable", "The .NET SDK did not return a local tool list.");
            var manifests = entries.EnumerateArray()
                .Where(item => item.TryGetProperty("packageId", out var id)
                    && string.Equals(id.GetString(), "RefTrace", StringComparison.OrdinalIgnoreCase)
                    && item.TryGetProperty("version", out var version)
                    && string.Equals(version.GetString(), installedVersion.Name, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.TryGetProperty("manifest", out var path) ? path.GetString() : null).Distinct().ToArray();
            if (manifests is not [string manifest] || !Path.IsPathFullyQualified(manifest) || !File.Exists(manifest))
                throw new InspectionException("unsupported-installation", "Cannot identify the manifest for this copy of RefTrace. Run the command in the project that pins it.");
            target = manifest;
            selector = "--tool-manifest";
        }

        var result = await ProcessCapture.RunAsync("dotnet", ["tool", "update", "RefTrace", selector, target, "--source", source],
            cwd, TimeSpan.FromMinutes(5), token).ConfigureAwait(false);
        return new(target, result.ExitCode == 0 ? "succeeded" : "failed", result.ExitCode, result.Output, result.Error);
    }

    // Windows locks the running shim. Let a worker outside the tool store wait for this process to exit
    // before the SDK replaces it. Inputs are environment data, never interpolated into PowerShell code.
    private static ToolUpdateReport ScheduleWindowsShimUpdate(string target, string source, string cwd)
    {
        var receipt = Path.Combine(Path.GetTempPath(), $"reftrace-update-{Guid.NewGuid():N}.json");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var worker = new ProcessStartInfo(powershell)
        {
            WorkingDirectory = cwd, UseShellExecute = false, StartDetached = true, InheritedHandles = []
        };
        worker.ArgumentList.Add("-NoProfile");
        worker.ArgumentList.Add("-NonInteractive");
        worker.ArgumentList.Add("-Command");
        worker.ArgumentList.Add("""
            $ErrorActionPreference = 'Stop'
            $code = 1
            try {
                $parent = Get-Process -Id $env:REFTRACE_UPDATE_PARENT -ErrorAction SilentlyContinue
                if ($parent) { $parent.WaitForExit() }
                $sdk = (Get-Command dotnet.exe -CommandType Application | Select-Object -First 1).Source
                $ErrorActionPreference = 'Continue'
                $messages = & $sdk tool update RefTrace --tool-path $env:REFTRACE_UPDATE_TARGET --source $env:REFTRACE_UPDATE_SOURCE 2>&1
                $code = $LASTEXITCODE
                $detail = ($messages | Out-String)
            } catch { $detail = $_.ToString() }
            $outcome = if ($code -eq 0) { 'succeeded' } else { 'failed' }
            $body = @{ outcome = $outcome; exitCode = $code; output = $detail } | ConvertTo-Json -Compress
            $file = $env:REFTRACE_UPDATE_RESULT
            [IO.File]::WriteAllText($file + '.tmp', $body)
            [IO.File]::Move($file + '.tmp', $file)
            exit $code
            """);
        worker.Environment["REFTRACE_UPDATE_PARENT"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        worker.Environment["REFTRACE_UPDATE_TARGET"] = target;
        worker.Environment["REFTRACE_UPDATE_SOURCE"] = source;
        worker.Environment["REFTRACE_UPDATE_RESULT"] = receipt;
        // Publish the final receipt by rename. A polling client cannot lock the destination mid-write.
        File.WriteAllText(receipt + ".tmp", "");
        try
        {
            using var process = Process.Start(worker) ?? throw new InspectionException("tool-unavailable", "Cannot start the Windows updater.");
            return new(target, "scheduled", ResultFile: receipt);
        }
        catch { File.Delete(receipt + ".tmp"); throw; }
    }
}
