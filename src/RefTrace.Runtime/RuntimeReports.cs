namespace RefTrace.Runtime;

public sealed record DiagnosticProcess(int ProcessId, string Name);
public sealed record RuntimeModule(long ModuleId, string IlPath, string NativePath);
public sealed record ObservedAssembly(int ClrInstanceId, long AssemblyId, long AppDomainId,
    string FullName, bool SeenInRundown, bool LoadObserved, bool UnloadObserved,
    IReadOnlyList<RuntimeModule> Modules);
public sealed record RuntimeReport(int ProcessId, string ProcessName, int ObserverProcessId,
    DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc, string Mechanism,
    bool RundownCompleted, long EventsLost, IReadOnlyList<ObservedAssembly> Assemblies,
    IReadOnlyList<string> Notes)
{
    public bool IsConclusive => RundownCompleted && EventsLost == 0 && Assemblies.Count != 0;
}

/// <summary>Accumulates copied event payloads on the EventPipe reader thread.</summary>
public sealed class RuntimeObservationBuilder
{
    private sealed class Entry(int clr, long assembly)
    {
        public int Clr { get; } = clr;
        public long Assembly { get; } = assembly;
        public long Domain { get; set; }
        public string? Name { get; set; }
        public bool Rundown { get; set; }
        public bool Loaded { get; set; }
        public bool Unloaded { get; set; }
        public Dictionary<long, RuntimeModule> Modules { get; } = [];
    }

    private readonly Dictionary<(int Clr, long Assembly), Entry> _entries = [];
    private readonly HashSet<int> _completedRuntimes = [];

    public void Assembly(int clr, long assembly, long domain, string name, bool rundown, bool unloaded = false)
    {
        var entry = Get(clr, assembly);
        entry.Domain = domain;
        entry.Name = name;
        entry.Rundown |= rundown;
        entry.Loaded |= !rundown && !unloaded;
        entry.Unloaded |= unloaded;
    }

    public void Module(int clr, long assembly, long module, string ilPath, string nativePath)
        => Get(clr, assembly).Modules[module] = new(module, ilPath, nativePath);

    public void CompleteRundown(int clr) => _completedRuntimes.Add(clr);

    public RuntimeReport Build(int pid, string name, int observerPid, DateTimeOffset started,
        DateTimeOffset finished, long lost)
    {
        var entries = _entries.Values.Where(e => e.Name is not null).ToArray();
        var complete = entries.Length != 0 && _entries.Values.All(e => e.Name is not null)
            && entries.All(e => _completedRuntimes.Contains(e.Clr));
        var notes = new List<string>
        {
            "Observed over an interval with end-of-session rundown; not an atomic memory snapshot.",
            "Assemblies come from the target process. Observer dependencies are not injected into it.",
            "AssemblyLoadContext names and dependencies between loaded assemblies are not inferred."
        };
        if (!complete) notes.Add("Complete CLR rundown was not observed; absence cannot be established.");
        if (lost != 0) notes.Add($"The EventPipe stream lost {lost} events; results are incomplete.");
        if (_entries.Values.Any(e => e.Name is null)) notes.Add("Some module events had no corresponding assembly identity.");
        return new(pid, name, observerPid, started, finished, "EventPipe loader events and CLR rundown",
            complete, lost, entries.OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Clr).ThenBy(e => e.Assembly)
                .Select(e => new ObservedAssembly(e.Clr, e.Assembly, e.Domain, e.Name!, e.Rundown, e.Loaded, e.Unloaded,
                    e.Modules.Values.OrderBy(m => m.IlPath, StringComparer.Ordinal).ThenBy(m => m.ModuleId).ToArray())).ToArray(), notes);
    }

    private Entry Get(int clr, long assembly)
    {
        var key = (clr, assembly);
        if (!_entries.TryGetValue(key, out var entry)) _entries.Add(key, entry = new(clr, assembly));
        return entry;
    }
}
