using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using RefTrace.Core;

namespace RefTrace.Runtime;

public static class RuntimeInspector
{
    public static IReadOnlyList<DiagnosticProcess> Processes()
    {
        var result = new List<DiagnosticProcess>();
        foreach (var pid in DiagnosticsClient.GetPublishedProcesses().Order())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                result.Add(new(pid, process.ProcessName));
            }
            catch (ArgumentException) { /* Process exited after discovery. */ }
            catch (InvalidOperationException) { /* Process exited after discovery. */ }
        }
        return result;
    }

    public static async Task<RuntimeReport> CaptureAsync(int processId, TimeSpan duration,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (processId <= 0) throw new InspectionException("invalid-argument", "--pid must be a positive process ID.");
        if (duration < TimeSpan.FromMilliseconds(100) || duration > TimeSpan.FromSeconds(30))
            throw new InspectionException("invalid-argument", "--seconds must be between 0.1 and 30.");
        if (timeout <= duration || timeout > TimeSpan.FromMinutes(5))
            throw new InspectionException("invalid-argument", "--timeout must exceed --seconds and cannot exceed 300 seconds.");

        using var process = Process.GetProcessById(processId);
        var processName = process.ProcessName;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var started = DateTimeOffset.UtcNow;
        var builder = new RuntimeObservationBuilder();
        try
        {
            var providers = new[]
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.Loader)
            };
            // Loader-only rundown avoids collecting method/JIT/GC data for this inventory.
            var configuration = new EventPipeSessionConfiguration(providers, 32,
                (long)ClrRundownTraceEventParser.Keywords.Loader | (long)ClrRundownTraceEventParser.Keywords.ForceEndRundown,
                requestStackwalk: false);
            var client = new DiagnosticsClient(processId);
            using var session = await client.StartEventPipeSessionAsync(configuration, deadline.Token).ConfigureAwait(false);
            using var source = new EventPipeEventSource(session.EventStream);
            source.Clr.LoaderAssemblyLoad += e => builder.Assembly(e.ClrInstanceID, e.AssemblyID, e.AppDomainID,
                e.FullyQualifiedAssemblyName, rundown: false);
            source.Clr.LoaderAssemblyUnload += e => builder.Assembly(e.ClrInstanceID, e.AssemblyID, e.AppDomainID,
                e.FullyQualifiedAssemblyName, rundown: false, unloaded: true);
            source.Clr.LoaderModuleLoad += e => builder.Module(e.ClrInstanceID, e.AssemblyID, e.ModuleID, e.ModuleILPath, e.ModuleNativePath);
            var rundown = new ClrRundownTraceEventParser(source);
            rundown.LoaderAssemblyDCStart += e => builder.Assembly(e.ClrInstanceID, e.AssemblyID, e.AppDomainID,
                e.FullyQualifiedAssemblyName, rundown: true);
            rundown.LoaderAssemblyDCStop += e => builder.Assembly(e.ClrInstanceID, e.AssemblyID, e.AppDomainID,
                e.FullyQualifiedAssemblyName, rundown: true);
            rundown.LoaderModuleDCStart += e => builder.Module(e.ClrInstanceID, e.AssemblyID, e.ModuleID, e.ModuleILPath, e.ModuleNativePath);
            rundown.LoaderModuleDCStop += e => builder.Module(e.ClrInstanceID, e.AssemblyID, e.ModuleID, e.ModuleILPath, e.ModuleNativePath);
            rundown.MethodDCStartComplete += e => builder.CompleteRundown(e.ClrInstanceID);
            rundown.MethodDCStopComplete += e => builder.CompleteRundown(e.ClrInstanceID);

            var reading = Task.Run(source.Process, CancellationToken.None);
            try
            {
                await Task.Delay(duration, deadline.Token).ConfigureAwait(false);
                await session.StopAsync(deadline.Token).ConfigureAwait(false);
                await reading.WaitAsync(deadline.Token).ConfigureAwait(false);
                return builder.Build(processId, processName, Environment.ProcessId, started, DateTimeOffset.UtcNow, source.EventsLost);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await session.StopAsync(cleanup.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is DiagnosticsClientException or IOException or OperationCanceledException) { }
                if (!reading.IsCompleted)
                {
                    await session.EventStream.DisposeAsync().ConfigureAwait(false);
                    try { await reading.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException) { }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InspectionException("timeout", "Runtime observation timed out before a complete stream was collected. Increase --timeout for a large process.");
        }
        catch (DiagnosticsClientException ex)
        {
            throw new InspectionException("diagnostics-unavailable", $"Cannot observe process {processId}: {ex.Message} The process must expose a compatible, accessible .NET diagnostic port. Native AOT or disabled diagnostics may not support this observation.");
        }
    }
}
