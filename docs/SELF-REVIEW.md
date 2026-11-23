# RefTrace self-review — 2026-10-29

The subsequent [transitive compatibility review](TRANSITIVE-REVIEW.md) supersedes
the six package retentions and the outstanding collection-expression suggestion
recorded below. This document preserves the initial 0.7.0 review results.
The current `global.json` pins SDK `11.0.100`, and `Directory.Packages.props` pins
System.CommandLine and Microsoft.DiaSymReader `3.0.0` and System.Security.Cryptography.Pkcs,
System.Security.Cryptography.ProtectedData and Microsoft.Extensions.DependencyModel
`11.0.0`; the rc.3 versions below are the ones reviewed then.

This run applied the complete [async/API modernization goal](ASYNC-THREADING.md)
to RefTrace itself, starting from `df9347e38a72478d906500e2decca5625af7ba28`.
The installed `ref` command performed the initial full review; the updated command
repeated release selection against the restored final graph. Raw package bytes,
source revisions, API comparisons, analyzer compiler inputs and logs stay under
`artifacts/api-review/20261029-self/` and are not checked in.

## Release selection

The full review covered **34 package coordinates** and **39 package archives**, with
**zero collection failures**. It found 15 newer listed candidates. Updating the owning
MSTest SDK resolved nine of those coordinates. The final catalog still covers 34
coordinates, with six deliberate transitive retentions and zero collection failures.

| Configured dependency | Selected version | Decision |
| --- | --- | --- |
| .NET SDK | 11.0.100-rc.3.26522.148 | Already the latest .NET 11 release in the official feed |
| MSTest.Sdk | 4.5.2 | Upgraded from 4.5.1; owns MSTest framework/adapter/analyzers 4.5.2 and MTP 2.5.2 |
| Meziantou.Analyzer | 3.0.317 | Already newest listed |
| NuGet.ProjectModel | 7.9.0 | Already newest listed |
| System.CommandLine | 3.0.0-rc.3.26522.148 | Already newest listed |
| System.Security.Cryptography.Pkcs | 11.0.0-rc.3.26522.148 | Already newest listed |
| Spectre.Console | 0.57.3-alpha.0.29 | Already newest listed |
| Microsoft.Diagnostics.NETCore.Client | 0.2.750501 | Already newest listed |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | Already newest listed |

> 2026-11-23: NuGet.ProjectModel is pinned at `8.0.0`, released on 2026-11-10; every NuGet
> package in the graph resolves to `8.0.0`. The `ref review` comparison of 7.9.0 with 8.0.0 is
> recorded in the [transitive review](TRANSITIVE-REVIEW.md#recheck-on-the-released-packages--2026-11-23).
> The 7.9.0 row above records what was newest on 2026-10-29.

“Newest” means a listed NuGet release selected with NuGet version ordering, including
newer prereleases. It does not mean a documentation framework selector or an unreleased
source branch. The [official .NET feed](https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json)
and [exact SDK source manifest](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/source-manifest.json)
resolve the runtime component to `474ed963699fc29809015392738a15231aaae2de`.

MSTest 4.5.2's package SHA-512 matches its catalog record. Its source revision is
`933377f07ac4bded77e820b0beef7d27e805cd89`. The SDK targets change default dependency
versions without changing routing logic. The [exact source comparison](https://github.com/microsoft/testfx/compare/0bf77e3aedbf86f1fa16220bc951d3b52ba56763...933377f07ac4bded77e820b0beef7d27e805cd89)
contains no net additions/removals in the core MSTest/MTP declared public API lists.
Package compatibility is also checked by building and running this repository's tests.

## Transitive candidates retained through their owners

| Package | Resolved → newer listed candidate | Why it remains at the owner's selection |
| --- | --- | --- |
| Microsoft.ApplicationInsights | 2.23.0 → 3.2.0 | MTP telemetry owns it. The candidate removes 632 declared signatures; overriding the major version has no established adapter compatibility evidence. |
| Microsoft.DiaSymReader | 2.2.10 → 3.0.0-rc.3.26522.148 | Current CodeCoverage 18.12.0 owns the symbol reader. RefTrace has no direct call requiring the RC; matching declarations do not prove native interoperability. |
| Microsoft.Extensions.DependencyModel | 10.0.10 → 11.0.0-rc.3.26522.148 | Current CodeCoverage owns it; no production call requires changing that dependency baseline. |
| Microsoft.TestPlatform.ObjectModel | 18.10.1 → 18.11.0 | MSTest 4.5.2 still selects the 18.10.1 test-platform line. No direct RefTrace calls. |
| Newtonsoft.Json | 13.0.3 → 14.0.1-beta2 | NuGet.ProjectModel/Packaging owns it. RefTrace serializes with System.Text.Json; the nine added declarations are unused here. |
| System.Security.Cryptography.ProtectedData | 8.0.0 → 11.0.0-rc.3.26522.148 | NuGet.Configuration owns it; the six added declarations are unused by RefTrace. |

These are explicit ownership decisions, not claims that all six candidates are
incompatible. Adding direct overrides would bypass the newest owners' chosen graphs.
The exact dependency paths and independently refreshed listing receipts are in
`versions/restored-owner-graph.json` and `versions/transitive-candidates.json`.

## Changes and retained contracts

- Deleted the `RefTrace.Core` forwarding project and its 16 type forwarders. Callers
  reference Model, Metadata and Assets directly; the common namespace remains usable.
- Deleted `CliApplication.Run` and its blocking `GetAwaiter().GetResult()` bridge.
  Program and all CLI tests await `RunAsync`, with cancellation passed through.
- Deleted `AssemblyInspector.AttributeTypeName` and `TypeName`; the existing
  `MetadataNames` decoder handles attribute constructors and nested names.
- Fixed EventPipe cleanup to observe completed and late reader failures without
  replacing the main operation's result. A nonthrowing observer stays attached if
  the two-second cleanup wait expires. Stream disposal releases pending reads.
- Fixed AOT output draining to cancel and await the actual `ReadAllTextAsync` reads
  after its five-second deadline, including inherited pipes left open by a child.
  Caller cancellation and incomplete output remain distinct report outcomes.

`ProcessCapture` remains: it owns argument/working-directory fidelity, stdout/stderr,
process-tree termination and timeout classification. RC3's
[Process convenience implementation](https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Scenarios.cs)
calls `Kill()` on the parent in its read-failure path; replacing this helper would lose
its descendant-cleanup contract. The actual
[pipe-reading implementation](https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Multiplexing.cs)
passes cancellation to both stream readers and joins them before returning.

TraceEvent's `Process()` is synchronous, so the single owned `Task.Run` remains.
`ApiSurface` and `MetadataNames` also remain separate: one preserves full declared
signatures; the other normalizes names for reflection-call classification. The IL
operand parser, input-validation gates, JSON properties consumed by generated
serializers and intentional metadata test fixtures are live code.

The [October 29 series index](https://www.meziantou.net/dotnet-11-runtime-async-the-runtime-takes-over-await.htm)
contains 19 published installments. Runtime async is available as an opt-in; this run
retains default compiler lowering. There is no application measurement justifying a
performance claim or a forced builder/ValueTask/custom-awaiter rewrite.

## Complete API and analyzer disposition

Every row A01–A44 has a final disposition below. The accompanying source inventory
contains 192 relevant production lines and 188 guard lines. Those are audit coverage,
not execution coverage. Explicit constants, mutable accumulators, protocol values and
indirect JSON consumers are accounted for in `api-audit/constant-inventory.md`.

| ID | Disposition | Production use and decision |
| --- | --- | --- |
| A01 | retain | Task and Task<T> compose command execution, MSBuild capture, EventPipe observation. Awaiting carries exceptions/cancellation; no ValueTask replacement justified by a measured synchronous completion profile. |
| A02 | absent | No production Task.CompletedTask or Task.FromResult. Present only in test/document examples; do not add caches without a hot-path case. |
| A03 | retain-framework-use | No declared production ValueTask return APIs; async stream disposal consumes BCL ValueTasks once. No AsTask in production. Keeping Task-based composition avoids single-consumption hazards. |
| A04 | absent | No IValueTaskSource implementation or direct consumer. Do not add specialized source ownership. |
| A05 | absent | No ManualResetValueTaskSourceCore implementation. The unsafe supplied guide signal was not imported. |
| A06 | absent | No production TaskCompletionSource. Tests use one-shot signaling only; not dead production. |
| A07 | absent | No explicit production TaskCreationOptions. No producer continuation policy to alter. |
| A08 | retain | Task.Run(source.Process, CancellationToken.None). Exact TraceEvent 4.0.0 source provides synchronous Process(); worker keeps caller able to issue timed StopAsync. None prevents cancellation before worker starts; completion is explicitly owned. |
| A09 | retain | Task.Delay(duration, deadline.Token). No Thread.Sleep. Represents real EventPipe observation interval; keep cancellable delay. |
| A10 | absent | No TimeProvider Delay overload. This is actual elapsed observation, not domain-clock policy. No fake clock is needed for public live-observation smoke. |
| A11 | absent | No production Task.WhenAll. Review project evaluations are sequential; Python collector owns bounded thread concurrency independently. |
| A12 | replace-implemented | Runtime reader cleanup always starts a nonfaulting observer over the original Task. CompleteReaderAsync always observes completion; base Task.ConfigureAwait(SuppressThrowing) marks exceptions handled even if the separate bounded wait times out. Primary capture await still propagates operational faults. |
| A13 | absent | No Task.WhenAny. No loser ownership wrapper to replace. |
| A14 | absent | No Task.WhenEach. No completion-order consumption use case. |
| A15 | retain-with-owned-completion | Runtime WaitAsync limits cleanup waiting while its observer retains future fault ownership; AOT no longer abandons a capture behind WaitAsync. AOT owns a linked capture CTS, starts its five-second timer only after joining the process, and awaits the actual capture. Runtime cleanup bounds caller latency separately from observing the underlying reader. |
| A16 | absent | No Parallel.ForEachAsync. The Python collector already bounds workers. No C# fanout requires introducing it. |
| A17 | absent | No production IAsyncEnumerable/await foreach. Capture requires both complete output strings, not streaming delivery. |
| A18 | absent | No EnumeratorCancellation or WithCancellation usage. No async iterator protocol to modernize. |
| A19 | retain-and-use-verified-overload | Library awaits remain ConfigureAwait(false). Cleanup observer uses non-generic Task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing). The base Task overload is intentional because generic Task<bool> cannot use SuppressThrowing. Exact RC3 TaskAwaiter.ValidateEnd marks exceptions as handled for this mode. |
| A20 | absent | No Channel.CreateBounded. No C# producer/consumer service or backlog to bound. |
| A21 | absent | No ChannelWriter/Reader calls. No channel shutdown contract. |
| A22 | absent | No channel flags. Do not import unused queue abstractions. |
| A23 | absent | No BackgroundService or generic host. CLI lifetime is command-owned. |
| A24 | absent | No DI scopes or IServiceScopeFactory. No scoped service lifetime. |
| A25 | absent | No Interlocked calls. RuntimeObservationBuilder mutates only on reader thread and is consumed after reader await; atomics do not replace that ownership. |
| A26 | absent | No C# Lock/lock/Monitor. No shared-state critical section; Python cache locks are outside this BCL checklist. |
| A27 | absent | No SemaphoreSlim or releaser. No obsolete mutex class exists. |
| A28 | absent | No Lazy<T>/Lazy<Task<T>>. No shared asynchronous initialization. |
| A29 | retain | Linked deadline CTS in process/AOT/runtime; two-second standalone cleanup CTS. Every source is using-owned. Cancellation is coupled to explicit process tree kill or session stop. No timed CTS leaks. |
| A30 | retain | Caller tokens propagated into MSBuild/process/stream/JSON/diagnostic session operations. CancellationToken.None is intentional only for cleanup that must finish after caller cancellation. Distinct timeout vs caller-cancellation outcomes retained. |
| A31 | absent | No explicit production Cancel/CancelAsync. Timer-driven cancellation is CTS behavior; no replacement required. |
| A32 | absent | No explicit production token registrations. BCL/diagnostic library registrations remain library-owned. |
| A33 | absent | No TimeProvider or PeriodicTimer direct use. Observation duration is wall-clock elapsed; no periodic scheduler. |
| A34 | absent | No Yield/ForceYielding. No custom switching or per-item suspension. |
| A35 | remove-implemented | No production GetAwaiter/GetResult wrapper remains. Deleted CliApplication.Run; production entry and tests await RunAsync. No invented custom awaiters. |
| A36 | absent | No ThreadPool queue calls or IThreadPoolWorkItem. No detached low-level work scheduler. |
| A37 | absent | No ExecutionContext suppression or AsyncLocal. No ambient-flow leak to repair. |
| A38 | retain | using owns PEReader, FileStream, JsonDocument, Process, diagnostic session/event source, CTS; await using owns async stream cleanup. Scopes enclose awaited use. No resource-returning Task escapes a using scope. |
| A39 | retain-framework-use | No explicit Flush/FlushAsync; File.WriteAllTextAsync and stream DisposeAsync are BCL-owned. No unsafe custom buffered sink exists. |
| A40 | remove-implemented | No production sync-over-async or ContinueWith remains. The only GetAwaiter().GetResult wrapper was removed. Synchronous PE/NuGet readers and required sync HelpAction remain synchronous APIs with actual callers. |
| A41 | absent | No custom AsyncMethodBuilder, pooling builder, IAsyncStateMachine implementations. Compiler-generated lowering is not dead hand-written code. |
| A42 | retain-default | No project-level runtime-async opt-in is enabled. Runtime async is an optional preview compiler feature. It does not obsolete source APIs or establish measured application benefit. Keep current lowering; do not claim benchmark improvements from documentation. |
| A43 | retain-explicit-ownership | Process convenience API verified present, but process owners retain explicit lifecycle. Exact RC3 read-failure cancellation path uses process.Kill() without entireProcessTree. Start/read/wait replacement would weaken tested descendant cleanup and pre-cancel behavior. |
| A44 | replace-capture-proxy-retain-process-owner | AOT now cancels/awaits original capture after process join. Existing explicit ProcessCapture remains for structured timeout versus cancellation and tree ownership. Old AOT capture.WaitAsync timeout could abandon a task that later faulted after stream disposal. New linked capture CTS reaches both ReadAsync operations via BCL Task.WhenAll and actual await observes completion. Process tree and partial-output outcome contracts remain explicit. |

All eight projects load Meziantou.Analyzer 3.0.317 (the Roslyn 5.9 analyzer build),
SDK analyzers, the intended package configuration and repository `.editorconfig`.
The test project additionally loads MSTest.Analyzers 4.5.2. No blanket suppression
or disabled analyzer execution was introduced.

| Analyzer policy | Verified result |
| --- | --- |
| MA0004, MA0022, MA0033, MA0040, MA0042, MA0064, MA0079, MA0100, MA0129, MA0134, MA0147, MA0155, MA0158, MA0166 | All 14 intentionally invalid examples emitted errors |
| MA0032 | Advisory in library code; exempt in tests |
| MA0045, MA0080, MA0137, MA0138 | Disabled by policy; positive controls emitted errors when enabled |
| CLI/test MA0004 exemptions | No diagnostics under repository policy; positive controls confirmed the underlying rule works |

MA0045 applies only to eligible non-public methods that can become async; it does
not diagnose public methods, overrides or interface implementations. The precise
policy and compiler evidence are saved in `analyzers/ledger.json` and
`analyzers/verified-wiring.json`.

## Validation

The final local Release build has zero warnings and errors. Native MTP executed
**122 tests: 122 passed, zero failed or skipped**. The collector's **19 Python tests**
passed. JetBrains inspection reports zero errors and warnings. Its informational
notes comprise 16 deliberate metadata-fixture hints and one optional collection-expression
style suggestion in `ProjectInputs`. The following cases exercise the affected behavior:

| Requirement | Evidence |
| --- | --- |
| Async CLI commands preserve output and exit semantics | `Assets_Json_HasVersionedSchemaAndGraphEvidence`, `Why_KnownAndMissingDependencies_HaveDistinctExitCodes`, and the migrated `CliTests` cases |
| Forwarded type metadata remains readable without the removed facade or loading a target | `Read_TypeForwarders_AreReportedWithoutLoadingTarget` |
| Shared attribute decoder preserves identity and attribute evidence | `Read_Assembly_SeparatesIdentityFrameworkAndInformationalVersions`, `Read_ReferenceAssemblyMarker_IsRecognizedWithoutResolvingAttributes`, `Read_AttributeScopeAndNamespace_AvoidFalseAssemblyClaims` |
| Cleanup preserves completed, canceled and faulted reader outcomes | `Cleanup_CompletedReaderPreservesOutcomeWithoutClosingStream` (four rows including an unexpected non-I/O failure) |
| Pending reader shutdown is observed after stream disposal | `Cleanup_DisposesPendingReaderAndObservesItsShutdownFailure` |
| Cleanup deadline preserves the original caller exception | `Cleanup_DeadlineDoesNotReplaceCallerCancellationOrWaitForLateReader` |
| AOT drain cancels pending reads after process exit and distinguishes timeout from caller cancellation | `Run_OpenInheritedPipeAfterExit_ReportsDrainDeadlineOrCallerCancellation` (two rows with real inherited pipes) |
| Existing process output and child termination behavior stays intact | `Run_CapturesBothLargeStreamsAndPreservesExitStatus`, `Run_CancellationOrDeadline_TerminatesTheProcessTree` |
| Real EventPipe collection still completes | `Capture_ControlledProcess_EnumeratesExistingAssembliesWithoutObserverDependencies` |

The late-reader observer's exception handling is additionally established by the
[exact TaskAwaiter source](https://github.com/dotnet/runtime/blob/474ed963699fc29809015392738a15231aaae2de/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs):
`SuppressThrowing` marks exceptions handled. The test that injects a fault after the
cleanup deadline proves prompt return and preservation of the original cancellation;
it does not measure the GC-driven unobserved-exception event. The two-second bound
applies to waiting for the reader after stream disposal, not to arbitrary third-party
`Stream.DisposeAsync` implementations.

Local commands: `make build`, `make test`, `make audit-test`, `make inspect`.
The final TRX is `artifacts/test-results.Mma2ij/RefTrace.Tests_net11.0_arm64.trx`.
CI and local-package installation are checked separately from source/package inspection.
No public NuGet release is part of this review. No performance improvement is claimed.
