# Transitive compatibility review — 2026-10-30

The 0.8.0 review tested all six candidates retained by the [0.7.0 self-review](SELF-REVIEW.md).
Five explicit transitive pins live in `Directory.Packages.props`, with
`CentralPackageTransitivePinningEnabled`. Application Insights was initially retained
because its newer major version failed the consumer check. The 0.8.1 follow-up below
removes the unused test telemetry extension and Application Insights altogether.

## Test telemetry removed in 0.8.1

RefTrace does not use Application Insights. `MSTest.TestAdapter` 4.5.2 brings in
`Microsoft.Testing.Extensions.Telemetry` 2.5.2, which brings in Application Insights
2.23.0. This extension reports usage of Microsoft's test platform; it does not power
RefTrace's EventPipe inspection, test execution, TRX reports or coverage collection.

The adapter has no assembly reference to the telemetry extension. The extension's
build props register its own generated startup hook. `Directory.Packages.props` now
uses `PrunePackageReference` to remove the optional extension through version 2.5.2
at restore time. Its Application Insights dependency and generated startup hook
then disappear. The bound is deliberate: review a later adapter dependency before
extending it. This uses NuGet's
[restore-time pruning](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#prunepackagereference).

The packaged `ref check` command now rejects either telemetry package in the test
graph during CI, so a future dependency change cannot silently reintroduce them.
The remaining MSTest extension profile still supplies TRX reporting and coverage.
The shipped CLI never contained either removed package; its runtime dependency
inventory is unchanged. Removal evidence is saved under
`artifacts/telemetry-removal-20261030/`.
(2026-11-23: that material was local and untracked, and it was deleted on 2026-11-23. It
no longer exists.)

The 0.8.1 Release build has zero warnings or errors. Normal execution and execution
with coverage both pass all 125 tests, with zero failures or skips; coverage contains
all six production modules. The 19 audit-tool tests pass. Across the eight current
projects, the package graph loses exactly the two telemetry packages and gains none.
The generated startup code and test dependency manifest contain neither extension
registration nor telemetry dependency. A refreshed catalog covers 32 coordinates
with zero newer candidates and zero collection failures. The dependency guard also
rejects a deliberately forbidden installed package as a positive control.
JetBrains inspection reports zero errors or warnings and the same 16 intentional
metadata-fixture notes.

## Initial review scope (0.8.0)

The review started at `f686a2e2de5eacbe31975ca60b4ce12e9fb34a62`. Fresh NuGet listing
receipts, package hashes, exact source, isolated probes, test results and diagnostic
stacks are under `artifacts/transitive-review-20261029/`; the directory name records
the UTC start date. Downloaded package source is not committed.
(2026-11-23: that material was local and untracked, and it was deleted on 2026-11-23. It
no longer exists.)

## Decisions

| Package and owner | Previous → candidate | Decision and evidence |
| --- | --- | --- |
| Microsoft.ApplicationInsights; MTP telemetry 2.5.2 | 2.23.0 → 3.2.0 | **Retain 2.23.0.** JIT preparation of the actual telemetry consumer throws `MissingMethodException` with 3.2.0. The three-argument `TelemetryClient.TrackEvent(string, IDictionary<string,string>, IDictionary<string,double>)` overload is missing. The same probe resolves with 2.23.0, which is also the newest listed 2.x release. |
| Microsoft.DiaSymReader; CodeCoverage 18.12.0 | 2.2.10 → 3.0.0-rc.3.26522.148 | **Upgrade.** No removed or added declared signatures; public-key token and all 33 DIA COM GUIDs match. The upgraded coverage graph successfully instruments and reports the portable-PDB fixture used by this project. |
| Microsoft.Extensions.DependencyModel; CodeCoverage 18.12.0 | 10.0.10 → 11.0.0-rc.3.26522.148 | **Upgrade.** No removed or added declared signatures. The combined coverage override passes four fixture tests, checks the loaded candidate assemblies, reads the real consumer dependency manifest and produces a nonempty report with both workload branches covered. |
| Microsoft.TestPlatform.ObjectModel; MSTest adapter 4.5.2 | 18.10.1 → 18.11.0 | **Upgrade.** The actual adapter registers 31 properties, discovers the same 122 baseline test names, display names and IDs, and executes all eight graph tests successfully in the isolated comparison. The expanded repository suite validates the combined graph. |
| Newtonsoft.Json; NuGet.ProjectModel/Packaging 7.9.0 | 13.0.3 → 14.0.1-beta2 | **Upgrade.** Nine added and no removed declared signatures. Actual NuGet lock-file and runtime-graph writers/readers preserve the asserted dependency graph, assets, imports, ranges and support contexts. |
| System.Security.Cryptography.ProtectedData; NuGet.Configuration 7.9.0 | 8.0.0 → 11.0.0-rc.3.26522.148 | **Upgrade.** Six added and no removed declared signatures. NuGet's byte-array overloads remain. Tests verify synthetic credential encryption in both directions with DPAPI on Windows and NuGet's expected platform rejection elsewhere. |

The telemetry probe never constructs a telemetry configuration, client or exporter.
It prepares the real consumer method and compares resolution of its referenced API;
turning off telemetry is not used as a compatibility fix.

DiaSymReader's legacy Windows-PDB/native COM fallback is outside this project's
portable-PDB execution evidence. Matching interfaces and COM GUIDs are evidence
about that boundary, not proof of every native reader implementation. The added
`CoCreateInstance` fallback requires `UseComRegistry`; CodeCoverage passes options
zero and does not request that changed branch. These decisions
were made against RefTrace's .NET 11 RC3/MSTest/NuGet graph; they have not been
rechecked since the pins moved to SDK 11.0.100 and the 11.0.0/3.0.0 packages.

> 2026-11-23: rechecked on the released packages and NuGet 8.0; see
> [the recheck below](#recheck-on-the-released-packages--2026-11-23). The decisions above
> keep their 2026-10-30 evidence.

## Recheck on the released packages — 2026-11-23

Rerun on SDK 11.0.100 with the graph of `77afe16` (NuGet.ProjectModel 8.0.0). Same five
upgrade pins, same owners, same consumer checks. The collector in `tooling/reftrace_audit`
downloaded each package again and matched its bytes with the NuGet catalog SHA-512, then
compared declared signatures from `ref assembly --type '*'`. Owners and requested versions
come from `ref assets tests/RefTrace.Tests/RefTrace.Tests.csproj --json`. The raw evidence
is local and untracked under `artifacts/transitive-review-20261123/`; the results are
recorded here so that this section does not depend on it.

| Package and owner (requested) | Owner's version → pin | Declared signatures | Consumer check | Decision |
| --- | --- | --- | --- | --- |
| Microsoft.DiaSymReader; CodeCoverage 18.12.0 (`[2.2.10, )`) | 2.2.10 → 3.0.0 | None added or removed, as with RC3; RC3 → 3.0.0 also none. Public-key token `31bf3856ad364e35` and all 33 COM GUIDs unchanged (`GuidAttribute` scan, outside RefTrace) | Full suite with `--coverage` on macOS ARM64: 156 passed; Cobertura for all six production modules | **Keep 3.0.0** |
| Microsoft.Extensions.DependencyModel; CodeCoverage 18.12.0 (`[10.0.10, )`) | 10.0.10 → 11.0.0 | None added or removed; RC3 → 11.0.0 none | Same coverage run | **Keep 11.0.0** |
| Microsoft.TestPlatform.ObjectModel; MSTest.TestAdapter 4.5.2 (`[18.10.1, )`) | 18.10.1 → 18.11.0 | None added or removed | The adapter discovers and runs all 156 tests | **Keep 18.11.0** |
| Newtonsoft.Json; NuGet.Packaging 8.0.0 (`[13.0.3, )`) | 13.0.3 → 14.0.1-beta2 | Nine added, none removed (unchanged package) | `DependencyCompatibilityTests` lock-file and runtime-graph round trips through NuGet 8.0.0's writers, including the new analyzer-asset writer | **Keep 14.0.1-beta2** |
| System.Security.Cryptography.ProtectedData; NuGet.Configuration 8.0.0 (`[8.0.0, )`) | 8.0.0 → 11.0.0 | Six added span overloads, none removed; RC3 → 11.0.0 none | `NuGetEncryption_RoundTripsOnWindowsAndRejectsUnsupportedPlatforms`: platform rejection on macOS here; the DPAPI round trip passed in the Windows job of [CI run 45163580603](https://github.com/ANcpLua/RefTrace/actions/runs/45163580603) | **Keep 11.0.0** |

What changed since 2026-10-30:

- The three .NET packages are the release builds. Each nuspec records
  `dotnet/dotnet@297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f` instead of `5254d8bc`, and
  their declared signatures equal those of the RC3 builds.
- The NuGet owners moved from 7.9.0 to 8.0.0. Their `net10.0` dependency groups request
  the same lower bounds as the 7.9.0 `net8.0` groups: Newtonsoft.Json 13.0.3 from
  Packaging and ProtectedData 8.0.0 from Configuration. The pins still raise exactly those
  two edges.
- Application Insights and the MTP telemetry extension remain absent from the test graph;
  the packaged `ref check` in CI enforces it.
- NuGet now lists newer candidates for owners and pins: MSTest 4.5.3 and MTP 2.5.3
  (published 2026-11-11) and TestPlatform.ObjectModel 18.12.0 (2026-11-11), which adds
  `EqtHash.GuidFromStringXxHash128` and removes nothing. This recheck does not adopt them;
  each needs its own review against its owner.

Not rechecked: the isolated coverage comparison with its portable-PDB fixture, the
adapter's 31 registered properties and the Application Insights probe. Their harnesses
lived in the deleted `artifacts/transitive-review-20261029/`. The full-suite coverage run
and CI's coverage on Linux, macOS and Windows ([run 45163580603](https://github.com/ANcpLua/RefTrace/actions/runs/45163580603)) are the
consumer evidence for this recheck.

## Sources

The RC3 DiaSymReader, DependencyModel and ProtectedData packages record
[`dotnet/dotnet@5254d8bc`](https://github.com/dotnet/dotnet/tree/5254d8bcf59917edc1e1abc33bdf2442b77b1c70).
Newtonsoft.Json records
[`52fa3aef`](https://github.com/JamesNK/Newtonsoft.Json/tree/52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa),
ObjectModel records
[`vstest@36206450`](https://github.com/microsoft/vstest/tree/36206450d47fec22e281ba9846445edc796f5080),
and Application Insights 3.2.0 records
[`c896b342`](https://github.com/microsoft/ApplicationInsights-dotnet/tree/c896b342f3c52766f148f9b3b1b3411e27c05da3).

Consumer checks use the pinned
[MTP telemetry implementation](https://github.com/microsoft/testfx/blob/933377f07ac4bded77e820b0beef7d27e805cd89/src/Platform/Microsoft.Testing.Extensions.Telemetry/AppInsightTelemetryClient.cs),
[NuGet lock-file implementation](https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.ProjectModel/LockFile/LockFileFormat.cs),
[runtime-graph implementation](https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Packaging/RuntimeModel/JsonRuntimeFormat.cs),
and [credential encryption implementation](https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Configuration/Utility/EncryptionUtility.cs).
Lock-file parsing alone would miss the Newtonsoft dependency: `Render`/`Write`
exercise it, while the parser uses System.Text.Json.

## Regression checks and style

`DependencyCompatibilityTests` retains three executable checks for NuGet lock files,
runtime graphs and credential encryption. The Windows case uses generated synthetic
text and the actual `CurrentUser`/`NuGet` entropy contract; no saved user credentials
or feeds are accessed. The normal repository suite now contains 125 tests.

CI collects Cobertura coverage on Linux, macOS and Windows, so the coverage packages
are exercised rather than merely restored. Third-party notices and the local preview
version are updated to 0.8.0-preview.1.

`ProjectInputs.IgnoredDirectories` now uses C# 15
[collection-expression arguments](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-15.0/collection-expression-arguments)
with `StringComparer.OrdinalIgnoreCase`. The existing ambiguous-project fixture uses
mixed/upper-case ignored directory names to catch accidental loss of that comparer.

The initial all-candidate run exposed a macOS RC3 process-reaping stall. Managed
stacks show `Process.StopTree` waiting for the shared process table while the SIGCHLD
reaper is in `CheckChildren`/`TryReapChild`; both fixture parents were stopped. Only
those disposable fixture processes were terminated to release the run. The three
methods that exercise process-tree cleanup now run outside parallel test execution.
This is a scheduling mitigation for the observed runtime behavior, not a replacement
for the runtime's process implementation or a claim that its underlying issue is fixed.

> 2026-11-23: .NET 11.0.0 carries the macOS fix (see the [async guide](ASYNC-THREADING.md)).
> `Run_CancellationOrDeadline_TerminatesTheProcessTree` runs in parallel again on SDK
> 11.0.100: 30 consecutive runs of `AotPublishInspectorTests` (14 tests, 15–16 s each) and
> 10 full-suite runs (156 tests, 25–26 s each) passed on macOS ARM64 without a stall, and
> [CI run 45163580603](https://github.com/ANcpLua/RefTrace/actions/runs/45163580603) passed the suite on Ubuntu, macOS and Windows.
> The other two methods keep `[DoNotParallelize]` for their own stated reasons.

## Local validation

- `make build`: zero warnings and errors with all five accepted overrides.
- `make test`: 125 passed, zero failed or skipped.
- `make audit-test`: 19 passed.
- `make inspect`: zero errors or warnings; the 16 remaining informational notes are
  intentional metadata fixtures. No production style suggestion remains.
- Full-suite coverage: 125 passed, zero failed or skipped; nonempty Cobertura output
  for all six RefTrace production modules. Both coverage runs and the normal run
  completed with the process-tree tests isolated from parallel execution.
- Isolated coverage comparison: three workload cases passed with each graph; the
  final candidate run also passed the assembly/manifest check, for four total.
  The workload had two of two fixture lines and branches covered.
- Fresh `ref review --catalog-only --refresh`: 34 coordinates, zero collection
  failures and one remaining update, Application Insights 3.2.0 (rejected above).

Packaged-tool smoke checks and cross-platform CI provide the final acceptance checks
for this preview. No public NuGet publication is part of this update.
