# .NET async and threading: verified reference and modernization goal

Last reviewed: **2026-10-29**. Scope: the APIs and patterns in the supplied
`dotnet11_async_bestinslot_reference.md`, plus their direct replacements in RefTrace.
This is a decision guide and a repeatable modernization task, not a claim that one
primitive is fastest for every workload or an inventory of the entire .NET API.

## Run the modernization goal

Paste this into a Codex chat opened in the repository you want to modernize. For
RefTrace, the reference is `docs/ASYNC-THREADING.md`. For another repository, attach
this file and substitute that repository's build and test entrypoints.

```text
/goal Modernize this repository's async, threading, cancellation, resource-lifetime,
and process APIs using docs/ASYNC-THREADING.md as a starting checklist. Verify every
recommendation against the actual installed or proposed dependency, then implement
the verified changes. Do not stop at an audit or a list of suggestions.

1. Read AGENTS.md, global.json, project files, central package versions and existing
   changes. Record the selected SDK, compiler, runtime, TFMs, RIDs, direct packages
   and relevant resolved transitive dependencies. Preserve unrelated work.
2. Start with ref review . --catalog-only --refresh to inventory listed package
   candidates. Use ref review . --package 'Package.Id' --refresh for package and
   signature evidence; review its failures and configured source-probe coverage.
   Refresh official release metadata, analyzer packages and the original article
   series. Include new installments. Resolve exact versions and source revisions.
   Prefer the newest released compatible version, including a newer preview/RC where this
   repository permits it. Never infer availability from a documentation date,
   framework selector, search snippet, unlisted package or the upstream main branch.
   Distinguish the newest release from what this checkout actually resolves.
3. Work through every API checklist row in this reference. Also inventory every
   production use, custom wrapper and implementation of those patterns, following
   callers, tests, generated inputs, DI, serialization and reflection where relevant.
   For each item record: current symbol and exact overload, callers, required
   behavior, proposed framework API, owning version, primary source, and disposition
   (replace, retain with reason, absent, or unverified with the missing evidence).
4. Replace redundant custom code with the verified framework API at its callers.
   Delete the obsolete method. If a class becomes redundant, delete the class; if
   a file becomes empty or wholly redundant, delete the file. Remove dead callers,
   imports, registrations, comments and tests of the removed implementation. Keep
   meaningful behavioral tests and adapt them to the replacement. Do not preserve
   a forwarding wrapper or compatibility shim without an actual consumer contract.
5. Preserve required cancellation, timeout, output, exception, ordering, boundedness,
   disposal and shutdown behavior. Read the replacement's implementation when those
   semantics matter. Compile the exact overload first. A shorter implementation or
   a newer name is not sufficient evidence of equivalence. Do not introduce custom
   awaiters, task sources, thread pools or copyable lock releasers as an optimization
   without a demonstrated need and correctness/performance evidence.
6. Make necessary dependency updates in their owning manifests: SDK in global.json,
   direct packages in Directory.Packages.props. Upgrade the owner of a transitive
   dependency before considering an override. Validate a proposed update before
   retaining it. Keep the ref command, readable help, and explicit JSON/plain modes.
   Refresh the analyzer policy below against the selected package's source. Check
   actual compiler inputs in every project, then use compile-only negative samples
   to prove the enabled rules and path exceptions work. Fix findings at their source;
   do not silence them globally or add speculative annotation exclusions.
7. In RefTrace use make build, make test and make audit-test. Add focused regressions
   for changed behavior, especially cancellation races, output from both streams,
   disposal on failure and remaining-task ownership. For other repositories use
   their prescribed checks. Do not add tests that merely reproduce implementation.
   Benchmark only when a performance claim drives a decision; record the actual
   environment and compare equivalent work. Never transplant .NET 10 timings into
   a .NET 11 guarantee.
8. Keep downloaded sources, packages, binlogs, raw reports and a per-item review
   ledger in a new artifacts/api-review/<run-id>/ directory. Refresh this reference's
   date, version baseline, source links, decisions and evidence limits. Do not copy
   package source into tracked files. Follow the repository's commit/push workflow;
   do not publish packages unless that release was explicitly requested.
9. Finish only after every API/analyzer checklist row and discovered in-scope
   production use has a recorded disposition, every justified replacement and resulting deletion is
   implemented, and the relevant checks pass. Report exact unresolved evidence or
   failed checks honestly; do not call them verified. Summarize removed methods,
   classes/files, retained exceptions, selected versions and test results.

This is one finite refresh and modernization run. Re-running the goal checks for
new releases and source changes again; the document does not monitor them itself.
```

## Verified version baseline

| Evidence | Observed value | Meaning |
| --- | --- | --- |
| Repository `global.json` and `dotnet --version` | `11.0.100-rc.3.26522.148`, roll-forward disabled | Selected build SDK |
| Official .NET 11 release feed | SDK `11.0.100-rc.3.26522.148`; runtime `11.0.0-rc.3.26522.148`; release date `2026-10-25` | Latest published .NET 11 channel entry when checked, not a permanent pin recommendation |
| Installed CoreCLR and ASP.NET Core shared framework | `11.0.0-rc.3.26522.148` | Available local runtime |
| Selected Roslyn compiler | `5.12.0-1.26522.148` | Actual compiler, rather than an inference from C# syntax |
| Installed `.version` revision | `5254d8bcf59917edc1e1abc33bdf2442b77b1c70` | Unified `dotnet/dotnet` source revision, **not** a `dotnet/runtime` commit |
| Unified source manifest's runtime revision | `474ed963699fc29809015392738a15231aaae2de` | Version-matched runtime source used below |
| Repository MSTest SDK | `4.5.1` | Existing test toolchain; not newly selected by this guide |
| Meziantou.Analyzer | `3.0.317`; source `0d0970fc06d68bc46210667b0eb0ee23c441e762` | Latest listed package verified through NuGet registration/catalog; selected analyzer |

Sources: [official release feed][releases], [unified source manifest][manifest].
Raw source receipts and local verification for this revision are under
`artifacts/async-reference-20261029/`. Artifacts are intentionally untracked.
Analyzer package/source receipts and compiler-input checks are under
`artifacts/analyzer-adoption-20261029/`. The package SHA-512 matched the NuGet
catalog; this verifies matching bytes, not reproducible source-to-binary provenance.
The installed reference XML can lag the assembly: compile the exact call before
declaring a preview API available or absent.

The series navigation now contains **19 installments**, including
[the October 29 runtime-async article][article-runtime].
[The October 27 task-elision article][article-elision] and
[October 28 benchmark article][article-benchmarks] are available in full.
The benchmark article explicitly reports **.NET 10** measurements. This revision
checks those three articles and relevant claims against runtime sources; it does
not claim a fresh line-by-line review of all 19 articles.

## Corrections to the supplied guide

| Original claim or implementation | Correct decision |
| --- | --- |
| Fixed nanosecond/byte rankings, “15x faster”, “30% less memory”, universal “50%” runtime-async improvement | Remove as selection rules. Results depend on hardware, runtime, contention, completion path, input and measurement method. A rounded zero is not literally free execution. [Benchmark context][article-benchmarks] |
| Async is *only* scalability; every Task maps to kernel I/O; exactly ten OS threads | Async represents completion and can improve responsiveness and overlap latency. `Task` also represents CPU work and manually completed operations. Timers, sockets and files have different implementations; thread counts are observations of a run, not contracts. [Task source][task] |
| `Task<T>` always allocates 72 B; all strings allocate and all integers are cached | False. `Task.FromResult` caches selected results, including null, booleans and a limited integer range in this revision. Cache details and object sizes are not portable API guarantees. [Factory implementation][task] |
| `ZeroAllocAsyncSignal` safely broadcasts and can be reset at any time | Delete it. One `ManualResetValueTaskSourceCore<T>` has one continuation slot per operation. Multiple waiters misuse it; resetting before consumption invalidates pending operations; `PulseAll` races with `Reset`. A version token does not enforce single consumption. [Task-source implementation][task-source] |
| Only `WithCancellation(ct).ConfigureAwait(false)` is legal | Both orders are supported. Each operation preserves the other setting. Passing a token still requires the producer to observe it. [Configured enumerable source][configured-enumerable] |
| `.ToArray()` is mandatory before `Task.WhenAll` | `WhenAll(IEnumerable<Task>)` enumerates its input itself. A snapshot is useful for ownership, repeated access and separating enumeration failure from starting work. A synchronously throwing delegate during task creation still needs handling. [Combinator source][task] |
| `WhenAll` swallows all but the first error | The aggregate task retains faults; `await` propagates an exception, not a promised chronological first error. Inspect `Exception` for the full fault set. [Combinator source][task] |
| `WaitAsync` fixes losing work left by `WhenAny` | It bounds the wait. It does not cancel the underlying operation or take ownership of its eventual result/fault. [Wait implementation][task] |
| `Lazy<Task<T>>` means lock-free initialization | The default Lazy mode synchronizes factory execution and caches the resulting Task, including its failure. Use an explicit retry policy if needed. [Lazy source][lazy] |
| Copyable `AsyncMutex.Releaser` guarantees one release; `maxCount: 1` fixes double release | Delete the copyable releaser. Copies can each release; a waiter can consume the first release before the duplicate, defeating the assumed protection. Use `WaitAsync` with `try/finally`. [Semaphore source][semaphore] |
| A custom ThreadPool awaiter guarantees zero allocation and is required | Delete the general-purpose wrapper. Delegate/state/queueing and surrounding async work may allocate. Use a Task-based work API or explicit yielding according to the actual scheduling contract. [Queue implementation][thread-pool] |
| `SuppressFlow` plus `UnsafeQueueUserWorkItem` provides leak-free detached work | Unsafe queueing already omits ExecutionContext capture. Suppression is redundant there, and neither API manages lifetime, failures, shutdown or captured objects. [Queue source][thread-pool], [context source][execution-context] |
| `SafeResource`/`SafeBufferedSink` always cleans up | A failed flush skips disposal in the supplied code. Dispose in a `finally`, or delegate ownership directly to `using`/`await using`. A Boolean flag alone does not promise concurrent disposal safety. [Disposal guidance][dispose] |
| Every task-returning method must become `async`; every synchronous wait deadlocks | Preserve lifetime scopes with `await`. Trivial forwarding can remain direct. Deadlock depends on context/scheduler and dependencies; blocking also consumes capacity even without deadlock. [Elision article][article-elision] |
| `FileOptions.Asynchronous` guarantees kernel async on every OS | It expresses the file's intended mode, not one universal implementation. Inspect the platform/file strategy before making an OS-level claim. |

Do not transplant `MyTask`, `MyThreadPool`, `MyCancellationToken`, custom builders
or the supplied signal/awaiter/mutex classes into production because they appear
in teaching material. Replace existing equivalents only after mapping their actual
contract and callers. A framework primitive and a domain-specific abstraction do
not automatically have interchangeable semantics.

## API-by-API checklist

The decisions below are source-based guidance at the recorded baseline. Each goal
run must record a disposition for every ID, including `absent` when the repository
does not use it. Split a row into separate ledger entries when different overloads
or callers have different contracts. `Task<T>` and `ValueTask<T>` variants are
included where stated. Custom sample names are not framework APIs.

| ID | API or construct | Current guidance and evidence |
| --- | --- | --- |
| A01 | `Task`, `Task<T>` | Default composable async result; retain ownership and observe completion. [Source][task] |
| A02 | `Task.CompletedTask`, `Task.FromResult<T>` | Use for completed results; avoid fixed cache/allocation assumptions. [Source][task] |
| A03 | `ValueTask`, `ValueTask<T>`, `AsTask()` | Use when the contract or measured completion profile warrants it. Consume once; convert once to Task when sharing/composing. [Source][value-task] |
| A04 | `IValueTaskSource<T>`: `GetResult`, `GetStatus`, `OnCompleted` | Specialized single-operation ownership; not a general shared Task substitute. [Implementation][task-source] |
| A05 | `ManualResetValueTaskSourceCore<T>`: `Version`, `Reset`, `SetResult`, `RunContinuationsAsynchronously` | Require exclusive lifecycle management and completed consumption before reuse. Reject the supplied broadcast implementation. [Source][task-source] |
| A06 | `TaskCompletionSource<T>`, `TrySetResult`, `TrySetException`, `TrySetCanceled` | Bridge callbacks; prefer asynchronously dispatched continuations when producer reentrancy is unsafe. Unregister every callback on all terminal paths. [Source][tcs] |
| A07 | `TaskCreationOptions.RunContinuationsAsynchronously` | Controls completion continuations; does not eliminate arbitrary user callbacks or every possible reentrancy. [Source][tcs] |
| A08 | `Task.Run` | Own and await CPU or unavoidable blocking work; do not wrap existing async I/O just to make it async. [Source][task] |
| A09 | `Thread.Sleep`, `Task.Delay` | Sleep blocks; Delay represents a timer. Prefer cancellable Delay for asynchronous waiting. [Task source][task] |
| A10 | `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` | Use an injected clock when timing needs deterministic control. [Source][task] |
| A11 | `Task.WhenAll` (generic/non-generic; collection overloads) | Join owned tasks; bound creation separately; inspect aggregate faults when reporting every failure. [Source][task] |
| A12 | `Task.Exception`, `AggregateException.InnerExceptions`, `Flatten()` | Diagnose the aggregate; flatten nested aggregates only if the reporting contract requires leaf exceptions. [Task source][task] |
| A13 | `Task.WhenAny` | Await the returned winner too. Explicitly handle losing operations; completion of the outer Task is not success of the winner. [Source][task] |
| A14 | `Task.WhenEach` | Consume completed Tasks, then await each result/fault. It neither throttles starts nor cancels remaining work. Simultaneous completion order is not a stable sort. [Source][task] |
| A15 | `Task.WaitAsync` / `Task<T>.WaitAsync` | Prefer built-in cancellation/timeout waits; use the TimeProvider overload when needed. Underlying work remains owned by its caller. [Source][task] |
| A16 | `Parallel.ForEachAsync`, `ParallelOptions.MaxDegreeOfParallelism`, `CancellationToken` | Bounded independent async bodies; choose concurrency from the downstream limit, not a universal benchmark ranking. [Source][parallel] |
| A17 | `IAsyncEnumerable<T>`, `IAsyncEnumerator<T>`, `await foreach` | Pull-based async sequence; terminate/dispose enumeration and define ownership on early exit. [Source][configured-enumerable] |
| A18 | `[EnumeratorCancellation]`, `WithCancellation` | Wire the enumerator token into a cancellable async iterator and observe it, including long synchronous loops. [Source][configured-enumerable] |
| A19 | `ConfigureAwait(bool)` on Task/ValueTask/enumerable/disposable | Choose based on whether continuation needs the captured context; false does not suppress ExecutionContext or guarantee a different thread. [Enumerable source][configured-enumerable], [context source][execution-context] |
| A20 | `Channel.CreateBounded<T>`, `BoundedChannelOptions`, `FullMode.Wait` | Producer/consumer coordination with capacity and backpressure; capacity bounds queued items, not all producer state. [Factory][channel], [implementation][bounded-channel] |
| A21 | `ChannelWriter<T>.WriteAsync`, `TryComplete`; `ChannelReader<T>.ReadAllAsync` | Await writes; finish the writer; propagate producer failures and define drain versus discard on shutdown. [Writer][channel-writer], [reader][channel-reader] |
| A22 | `SingleReader`, `SingleWriter`, `AllowSynchronousContinuations` | Set only promises the application actually satisfies; synchronous continuations change reentrancy. [Implementation][bounded-channel] |
| A23 | `BackgroundService.ExecuteAsync`, `StopAsync` | Host owns the service task. Define fault and shutdown policy; cancellation can abandon queued items. [Source][background-service] |
| A24 | `IServiceScopeFactory`, `CreateAsyncScope`, `AsyncServiceScope.DisposeAsync` | Create a scope per scoped work lifetime; await async disposal. [Extensions source][scope] |
| A25 | `Interlocked.Increment`, `Exchange`, `Decrement` | Atomic operation on a value; not a replacement for multi-field invariants. [Source][interlocked] |
| A26 | `System.Threading.Lock`, `lock`; `Monitor` | Short synchronous critical sections; no await inside lock. A statically typed Lock uses its specialized scope. [Lock source][lock] |
| A27 | `SemaphoreSlim.Wait`, `WaitAsync`, `Release`, `Dispose` | Async mutual exclusion uses WaitAsync then try/finally Release. Dispose only after users finish. [Source][semaphore] |
| A28 | `Lazy<Task<T>>`, `Lazy<T>.Value` | Single shared initialization; default mode synchronizes; failed Task is cached. [Source][lazy] |
| A29 | `CancellationTokenSource`, `CancelAfter`, timed TimeProvider constructor | Own/dispose the source; cancellation is cooperative. A timer firing does not prove the operation stopped. [Source][cts] |
| A30 | `CreateLinkedTokenSource`, `Token`, `IsCancellationRequested`, `ThrowIfCancellationRequested` | Propagate cancellation and dispose links; preserve which token/operation caused a failure. [Source][cts] |
| A31 | `Cancel()`, `CancelAsync()` | Choose whether to wait asynchronously for callbacks. CancelAsync does not await all operations that happen to use the token. [Source][cts] |
| A32 | `CancellationToken.Register`, registration `Dispose`/`DisposeAsync` | Registration may invoke immediately for an already-canceled token. Cleanup must respect in-flight callbacks and lifetime. [Cancellation source][cts] |
| A33 | `TimeProvider`, `PeriodicTimer.WaitForNextTickAsync` | Built-in clock/timer primitives; timer is single-consumer and coalesces ticks. [Timer source][periodic-timer] |
| A34 | `Task.Yield`, `ConfigureAwaitOptions.ForceYielding` | Select yielding semantics deliberately. ForceYielding without ContinueOnCapturedContext avoids requesting that context; no zero-cost or new-thread promise. [Options source][configure-options] |
| A35 | `GetAwaiter`, `IsCompleted`, `GetResult`, `INotifyCompletion`, `ICriticalNotifyCompletion` | Awaiter protocol, not justification to invent an awaitable. UnsafeOnCompleted is optional specialization, not mandatory for every awaiter. [ValueTask source][value-task] |
| A36 | `ThreadPool.QueueUserWorkItem` / `UnsafeQueueUserWorkItem`, `IThreadPoolWorkItem` | Specialized scheduling with explicit failure/lifetime ownership. Unsafe variants omit ExecutionContext capture; callback/state wrappers may allocate. [Source][thread-pool] |
| A37 | `ExecutionContext.SuppressFlow`, `AsyncFlowControl.Dispose`, `AsyncLocal<T>` | Suppression scope stays synchronous/on its originating thread; distinguish context flow from captured references. [Source][execution-context] |
| A38 | `IDisposable`, `IAsyncDisposable`, `using`, `await using` | Await completion inside resource lifetime; guarantee disposal even if earlier cleanup fails. [Guidance][dispose] |
| A39 | `Stream.Flush`, `FlushAsync`, `Dispose`, `DisposeAsync` | Flush failure must not skip owned-resource disposal. Do not invent synchronous-over-async disposal when a synchronous API exists. [Source][stream] |
| A40 | `Task.Result`, `Wait`, `GetAwaiter().GetResult`, `ContinueWith` | Review blocking and exception/scheduler contracts; prefer await for async flow. A completed-result read is a different case from blocking. [Source][task] |
| A41 | `AsyncMethodBuilder`, pooling builders, `IAsyncStateMachine` | Advanced implementation mechanisms. Do not globally swap builders or preserve toy implementations without evidence. Runtime async changes lowering. [Runtime guidance][runtime-async] |
| A42 | `runtime-async=on`, SDK/compiler feature selection | Optional project-level lowering choice; verify build and behavior on the selected runtime. Do not infer measured application benefit. [Runtime guidance][runtime-async] |
| A43 | `Process.RunAndCaptureTextAsync`, `ProcessTextOutput`, `ProcessExitStatus` | Candidate replacement for handwritten start/read/wait helpers. Verify redirects, cancellation result, process-tree ownership and output draining. [Exact implementation][process-scenarios] |
| A44 | `Process.ReadAllTextAsync`, `WaitForExitAsync`, `Kill(entireProcessTree: true)` | Keep explicit process ownership if required cleanup differs from convenience APIs; cancellation of a wait alone does not kill a process. [Capture implementation][process-capture] |

## Analyzer policy: verified against Meziantou.Analyzer 3.0.317

RefTrace references the analyzer once through `GlobalPackageReference` in
`Directory.Packages.props`. NuGet applies it to all nine projects as a private
development dependency. `MeziantouAnalysisMode=None` in `Directory.Build.props`
disables that package's default rule set; the root `.editorconfig` explicitly
enables the reviewed rules below. This setting does not disable the SDK, MSTest
or other analyzers. [Package][analyzer-package], [package configuration][analyzer-props]

The installed compiler selects the package's `roslyn5.9` analyzer assemblies.
Resolved `Analyzer` and `EditorConfigFiles` items were checked for all nine projects,
including both the package's `none.editorconfig` and the repository policy.

### Rule checklist

The package-default column describes the untouched package, not RefTrace's policy.
In this repository `TreatWarningsAsErrors=true`, so both configured `warning` and
`error` severities fail a build when a diagnostic applies. `suggestion` remains advisory.
Defaults were checked against the [packaged default configuration][analyzer-defaults]
and the rule descriptors at the recorded source revision.

| Rule | Check | Package default | RefTrace policy |
| --- | --- | --- | --- |
| MA0004 | Explicit context choice with ConfigureAwait | warning, enabled | warning; Always in libraries, disabled in CLI/tests |
| MA0022 | Do not return null from Task-returning methods | warning, enabled | error |
| MA0032 | Token overload when no token is available | suggestion, disabled | suggestion; disabled in tests |
| MA0033 | No ThreadStatic on instance fields | warning, enabled | error |
| MA0040 | Forward an available cancellation token | suggestion, enabled | warning |
| MA0042 | Avoid blocking calls in async code | suggestion, enabled | error |
| MA0045 | Find async alternatives in synchronous methods | suggestion, disabled | disabled; changing the API contract requires review |
| MA0064 | No locking on publicly accessible instances | warning, enabled | error |
| MA0079 | Forward an available token to async enumeration | suggestion, enabled | warning |
| MA0080 | Enumeration token when no token is available | suggestion, disabled | disabled; signature changes require review |
| MA0100 | Await before leaving a resource lifetime | warning, enabled | error |
| MA0129 | Await the task used in a using statement | warning, enabled | error |
| MA0134 | Observe awaitable calls in synchronous contexts | warning, enabled | warning; report explicit discards too |
| MA0137 | Async suffix on awaitable methods | warning, disabled | disabled; naming is outside this change |
| MA0138 | No Async suffix on non-awaitable methods | warning, disabled | disabled; naming is outside this change |
| MA0147 | No async-void delegate passed as a synchronous delegate | warning, enabled | error |
| MA0155 | No async-void method/local-function declarations | warning, **disabled** | error |
| MA0158 | Use System.Threading.Lock where applicable | warning, enabled | warning |
| MA0166 | Forward an available TimeProvider | suggestion, enabled | warning |

Important corrections to the supplied configuration:

- The shared optional-overload key is **MA0032.allow_overloads_with_optional_parameters**.
  It controls MA0032 and MA0040. `MA0040.allow_overloads_with_optional_parameters`
  is not read by this version. Namespace-search options are separate for each rule.
  [Source][analyzer-cancellation]
- MA0155 is disabled by default, and its implementation has no automatic event-handler
  exemption. If a real event requires async void, review that exact boundary and any
  narrow suppression; do not assume the analyzer recognizes it. [Source][analyzer-void]
- MA0100 checks resource lifetime, not every direct Task return. Its explicit
  AsyncFlowControl exception allows a synchronous SuppressFlow scope. [Rule][analyzer-lifetime]
- MA0134 runs in synchronous contexts. Compiler CS4014 covers unawaited calls in async
  methods, but an explicit discard in async code can evade both. Assignment alone
  does not prove eventual observation, cancellation or shutdown ownership. [Rule][analyzer-observe]
- MA0004 accepts an explicit context choice; it does not prove that false is correct
  for every UI or application callback. MA0166 also has an
  `include_extension_methods_from_not_imported_namespaces` option. [Rules][analyzer-rules]
- Path patterns must include files directly inside a project as well as deeper files.
  The checked policy uses `src/RefTrace.Cli/**.cs` and `tests/**.cs` for those scopes.

### Reusable configuration

For another CPM repository, add this central reference and set the analysis mode in
its shared build properties, then use the policy below. Keep the version subject to
the goal's freshness and compatibility checks. Adapt the CLI/test paths to that
repository; do not assume every `src` directory contains libraries.

```xml
<!-- Directory.Packages.props: inside an ItemGroup -->
<GlobalPackageReference Include="Meziantou.Analyzer" Version="3.0.317" />

<!-- Directory.Build.props: inside a PropertyGroup -->
<MeziantouAnalysisMode>None</MeziantouAnalysisMode>
```

The following block is the checked-in `.editorconfig` policy:

<!-- analyzer-policy:start -->
```ini
root = true

# Meziantou.Analyzer 3.0.317: selected async/threading rules.
# Other analyzers keep their existing defaults. See docs/ASYNC-THREADING.md.
[*.cs]
dotnet_diagnostic.MA0004.severity = warning
MA0004.report = DetectContext

# Forward an available token/clock. Missing-token API design stays advisory.
dotnet_diagnostic.MA0032.severity = suggestion
dotnet_diagnostic.MA0040.severity = warning
dotnet_diagnostic.MA0079.severity = warning
dotnet_diagnostic.MA0166.severity = warning
# This is the shared option for MA0032 and MA0040 in the installed package.
MA0032.allow_overloads_with_optional_parameters = true
MA0032.include_extension_methods_from_not_imported_namespaces = false
MA0040.include_extension_methods_from_not_imported_namespaces = false

dotnet_diagnostic.MA0042.severity = error
MA0042.enable_sqlite_special_cases = true
MA0042.enable_db_special_cases = true
MA0042.include_extension_methods_from_not_imported_namespaces = false
MA0042.report_collection_initializers = false

# Scope/lifetime and unobserved-work errors are build failures.
dotnet_diagnostic.MA0022.severity = error
dotnet_diagnostic.MA0100.severity = error
dotnet_diagnostic.MA0129.severity = error
dotnet_diagnostic.MA0134.severity = warning
MA0134.report_discarded = true
dotnet_diagnostic.MA0147.severity = error
dotnet_diagnostic.MA0155.severity = error
dotnet_diagnostic.MA0033.severity = error
dotnet_diagnostic.MA0064.severity = error
dotnet_diagnostic.MA0158.severity = warning

# Synchronous APIs and naming changes require a separate contract decision.
dotnet_diagnostic.MA0045.severity = none
dotnet_diagnostic.MA0080.severity = none
dotnet_diagnostic.MA0137.severity = none
dotnet_diagnostic.MA0138.severity = none

[src/**.cs]
MA0004.report = Always

# The CLI owns its execution context; reusable libraries use ConfigureAwait.
[src/RefTrace.Cli/**.cs]
dotnet_diagnostic.MA0004.severity = none

# The test runner owns test continuations. Keep lifetime/cancellation checks on.
[tests/**.cs]
dotnet_diagnostic.MA0004.severity = none
dotnet_diagnostic.MA0032.severity = none
```
<!-- analyzer-policy:end -->

### Annotations are explicit exceptions

`Meziantou.Analyzer.Annotations` 1.8.0 was inspected separately but is not needed by
this change. It provides `ExcludeFromCancellationTokenAnalysis`,
`ExcludeFromBlockingCallAnalysis` and `NonAsyncDisposableType` as described in the
supplied reference. Use them only for a documented concrete exception, preferably
the exact overload. Do not globally exclude DbCommand or unrelated provider APIs
to make a build pass. NonAsyncDisposableType uses exact-type matching, not a blanket
exclusion for all derived types.

The package removes attribute **usages** from emitted metadata by default through
conditional attributes. Defining `MEZIANTOU_ANALYZER_ANNOTATIONS` retains them;
copied attribute type declarations are not automatically deleted. An omitted
attribute also cannot configure a downstream consumer through metadata. Inspect
packaged outputs separately before claiming no shipped dependency.
[Version-matched annotation guide][analyzer-annotations]

## Small patterns to retain

The examples below are independent compilation units. They are intentionally
small: integrate the framework calls into the domain method instead of adding
another generic helper class to production. No allocation/timing guarantee is made.

### Async mutual exclusion without a copyable releaser

```csharp
public sealed class SerializedOperation : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await operation(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The owner must finish all RunAsync calls before disposing this instance.
    public void Dispose() => _gate.Dispose();
}
```

### A shared signal uses a shared Task

Use a `TaskCompletionSource<T>` for one shared completion. If the domain truly needs
a resettable level-triggered event, separate generations and keep all accesses under
one gate. `Reset` while unsignaled retains existing waiters. This intentionally
allocates a new source per completed generation; it is not a zero-allocation primitive.

```csharp
public sealed class AsyncManualResetEvent
{
    private readonly System.Threading.Lock _gate = new();
    private TaskCompletionSource<bool> _source = CreateSource();

    public Task WaitAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return _source.Task.WaitAsync(ct);
    }

    public void Set()
    {
        lock (_gate)
            _source.TrySetResult(true);
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_source.Task.IsCompleted)
                _source = CreateSource();
        }
    }

    private static TaskCompletionSource<bool> CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
```

Cancellation cancels one wait, not the shared event. Completion wins if the captured
Task is already complete. This event is level-triggered, not a counting semaphore
or a queue; use those framework primitives for their own semantics.

### Disposal still happens when flushing fails

```csharp
public sealed class OwnedBufferedStream(Stream stream) : IDisposable, IAsyncDisposable
{
    private Stream? _stream = stream;

    public void Dispose()
    {
        Stream? owned = Interlocked.Exchange(ref _stream, null);
        if (owned is null) return;
        try { owned.Flush(); }
        finally { owned.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        Stream? owned = Interlocked.Exchange(ref _stream, null);
        if (owned is null) return;
        try { await owned.FlushAsync().ConfigureAwait(false); }
        finally { await owned.DisposeAsync().ConfigureAwait(false); }
    }
}
```

The owner still serializes use with disposal. Exchanging the reference prevents
duplicate ownership; concurrent disposal callers do not share an awaited completion.
If both flush and disposal throw, the disposal exception wins under ordinary finally
semantics. Choose a different explicit error policy if the domain needs both errors.

## Review evidence and completion criteria

For each run, retain a ledger with columns:

`ID | production symbol/overload | caller | old behavior | candidate/version |
source revision | compile evidence | behavioral evidence | disposition | reason`

For deletion, confirm that no remaining caller, registration, generated input or
required external contract needs the old member. Keep genuine domain responsibilities.
`absent` is a valid disposition; adding every checklist primitive is not the goal.

Before promoting a changed pattern, exercise the behavior that justified it:

- Shared signal: multiple existing waiters, reset before completion, a new generation,
  and one canceled waiter that does not cancel the others.
- Mutual exclusion: cancellation before acquisition must not release; exceptions after
  acquisition must release; disposing the owner requires quiescence.
- Disposal: flush failure must still dispose on both paths; repeated disposal does not
  dispose the inner resource again.
- Composition: zero/one/many tasks, synchronous task-factory failure, several asynchronous
  faults, caller cancellation, and ownership of work left after early exit.
- Process capture: stdout and stderr larger than pipe buffers, nonzero exit, pre-cancel,
  cancellation while running, process cleanup, working directory and argument fidelity.

No new benchmark result is claimed by this reference. Metadata inspection, compilation,
behavioral tests, published benchmark numbers and an application performance measurement
are different evidence and must stay labeled separately.

### RefTrace application and validation on 2026-10-29

`AotPublishInspector` now calls `Process.ReadAllTextAsync` for the SDK probe,
MSBuild evaluation and publish output. This removes the hand-managed pair of stream
reader tasks and their joining code. It also supplies arguments directly through
`ProcessStartInfo` and joins a canceled probe process after killing it. Publish output
has one five-second drain deadline for both streams after process completion.

The process-owning helper remains because it enforces whole-process-tree cleanup.
The inspected `RunAndCaptureTextAsync` implementation calls `Kill()` on capture
failure; that does not replace RefTrace's `Kill(entireProcessTree: true)` contract.
Its cancellation can also throw while reading, so treating every cancellation as a
returned `ProcessExitStatus.Canceled` would be incorrect. [Source][process-scenarios]

`RuntimeInspector` retains `Task.Run(source.Process)`: TraceEvent exposes a synchronous
stream processor here, so removing the worker would block the observation flow.
The supplied custom signal, copyable mutex releaser, custom awaiter and detached
dispatcher classes were not present in RefTrace production code. Their unsafe
versions were removed from this rewritten guide; there was no corresponding
production class or file to delete.

| Validation | Result |
| --- | --- |
| `make build` | Passed, zero warnings/errors |
| `make test` | 99 passed, zero failed/skipped |
| `make audit-test` | 13 passed |
| Analyzer compiler inputs | `3.0.317` / Roslyn 5.9 assemblies and intended config files resolved in all nine projects |
| Compile-only negative analyzer samples | All 14 enforced rules produced diagnostics; CLI/test ConfigureAwait exceptions applied |
| Three C# examples extracted from this document | Compiled; five focused behavior checks passed with default compilation and again with `runtime-async=on` |
| `Run_CapturesBothLargeStreamsAndPreservesExitStatus` | Success and nonzero-exit rows; 128 KiB per stream, Unicode, working directory and argument with spaces preserved |
| `Run_CancellationOrDeadline_TerminatesTheProcessTree` | Caller-cancellation and timeout rows both passed with a real controlled child process |

These executions were on macOS ARM64/CoreCLR with the SDK above. Windows, Linux,
Native AOT, arbitrary process-output encodings and application performance were not
validated by this run. `ReadAllTextAsync` uses the selected process encoding; unlike
the former StreamReader calls, its inspected implementation does not autodetect a
different encoding from an output BOM. The tested contract is normal .NET CLI text
output. Recheck encoding requirements before applying this replacement elsewhere.
The latest .NET result file is `artifacts/test-results.POtxIL/RefTrace.Tests_net11.0_arm64.trx`.
The analyzer found two synchronous fixture file writes, now awaited, and missing
tokens in runtime-test cleanup. Cleanup now has its own three-second cancellation
source so a canceled test still terminates and joins its child process.

[analyzer-package]: https://www.nuget.org/packages/Meziantou.Analyzer/3.0.317
[analyzer-props]: https://github.com/meziantou/Meziantou.Analyzer/blob/0d0970fc06d68bc46210667b0eb0ee23c441e762/src/Meziantou.Analyzer.Pack/build/Meziantou.Analyzer.props
[analyzer-defaults]: https://github.com/meziantou/Meziantou.Analyzer/blob/0d0970fc06d68bc46210667b0eb0ee23c441e762/src/Meziantou.Analyzer.Pack/configuration/default.editorconfig
[analyzer-rules]: https://github.com/meziantou/Meziantou.Analyzer/tree/0d0970fc06d68bc46210667b0eb0ee23c441e762/docs/Rules
[analyzer-cancellation]: https://github.com/meziantou/Meziantou.Analyzer/blob/0d0970fc06d68bc46210667b0eb0ee23c441e762/src/Meziantou.Analyzer/Rules/UseAnOverloadThatHasCancellationTokenAnalyzer.cs
[analyzer-void]: https://github.com/meziantou/Meziantou.Analyzer/blob/0d0970fc06d68bc46210667b0eb0ee23c441e762/src/Meziantou.Analyzer/Rules/DoNotUseAsyncVoidAnalyzer.cs
[analyzer-lifetime]: https://github.com/meziantou/Meziantou.Analyzer/blob/0d0970fc06d68bc46210667b0eb0ee23c441e762/docs/Rules/MA0100.md
[analyzer-observe]: https://github.com/meziantou/Meziantou.Analyzer/blob/0d0970fc06d68bc46210667b0eb0ee23c441e762/docs/Rules/MA0134.md
[analyzer-annotations]: https://github.com/meziantou/Meziantou.Analyzer/blob/b20a548d7a299229b245396ac33e7064c6e8a03b/src/Meziantou.Analyzer.Annotations/README.md

[releases]: https://raw.githubusercontent.com/dotnet/core/main/release-notes/11.0/releases.json
[manifest]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/source-manifest.json
[runtime-async]: https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime
[process-guide]: https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries#process-api-expansion
[process-scenarios]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Scenarios.cs
[process-capture]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Multiplexing.cs
[dispose]: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync
[article-runtime]: https://www.meziantou.net/dotnet-11-runtime-async-the-runtime-takes-over-await.htm
[article-elision]: https://www.meziantou.net/prefer-async-await-over-directly-returning-task.htm
[article-benchmarks]: https://www.meziantou.net/the-cost-of-async-a-benchmarkdotnet-study-on-dotnet-10.htm
[task]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs
[value-task]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ValueTask.cs
[task-source]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs
[tcs]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskCompletionSource_T.cs
[configured-enumerable]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/ConfiguredCancelableAsyncEnumerable.cs
[parallel]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Threading.Tasks.Parallel/src/System/Threading/Tasks/Parallel.ForEachAsync.cs
[channel]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Threading.Channels/src/System/Threading/Channels/Channel.cs
[bounded-channel]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Threading.Channels/src/System/Threading/Channels/BoundedChannel.cs
[channel-writer]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Threading.Channels/src/System/Threading/Channels/ChannelWriter.cs
[channel-reader]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Threading.Channels/src/System/Threading/Channels/ChannelReader.cs
[background-service]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs
[scope]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ServiceProviderServiceExtensions.cs
[interlocked]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Interlocked.cs
[lock]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Lock.cs
[semaphore]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/SemaphoreSlim.cs
[lazy]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Lazy.cs
[cts]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs
[periodic-timer]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/PeriodicTimer.cs
[configure-options]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ConfigureAwaitOptions.cs
[thread-pool]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs
[execution-context]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs
[stream]: https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/IO/Stream.cs
