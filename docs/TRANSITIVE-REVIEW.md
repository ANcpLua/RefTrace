# Transitive compatibility review — 2026-10-30

This follow-up tested all six candidates retained by the [0.7.0 self-review](SELF-REVIEW.md).
The owning packages and SDK are already current. Five explicit transitive pins now
live in `Directory.Packages.props`, with `CentralPackageTransitivePinningEnabled`.
Application Insights remains at the version selected by its owner because the newer
major version fails an actual consumer compatibility check.

The review started at `f686a2e2de5eacbe31975ca60b4ce12e9fb34a62`. Fresh NuGet listing
receipts, package hashes, exact source, isolated probes, test results and diagnostic
stacks are under `artifacts/transitive-review-20261029/`; the directory name records
the UTC start date. Downloaded package source is not committed.

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
apply to RefTrace's pinned .NET 11 RC3/MSTest/NuGet graph.

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
