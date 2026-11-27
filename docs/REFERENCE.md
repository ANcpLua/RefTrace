# Technical reference

This document covers report details, limitations, build setup, and packaging.
Start with the [README](../README.md) for everyday commands.

Run the development commands below from the RefTrace repository root.

Trace resolved .NET dependencies and inspect assembly metadata from the command line.
RefTrace reads existing artifacts and can explicitly observe a live .NET process.
Explicit artifact inspection does not restore projects, load target assemblies, or invoke their code.
Project inputs evaluate MSBuild properties without running build targets; imported project
configuration and property functions are evaluated in the project's existing environment.
Live observation is a separate command using the target's diagnostics endpoint.

Requires the .NET 11 runtime. Development uses the SDK pinned in `global.json`.

## Commands

```sh
ref assets path/to/project.assets.json --framework net10.0
ref packages path/to/project.assets.json --framework net10.0
ref why System.Security.Cryptography.Pkcs path/to/project.assets.json --framework net10.0
ref check path/to/project.assets.json --forbid-package 'Basic.Reference.Assemblies.*'
ref check path/to/project.assets.json --forbid-project '*Other.Tests.csproj'
ref assembly path/to/library.dll
ref assembly path/to/library.dll --type 'Some.Namespace.Outer+Nested'
ref sdk path/to/App.csproj
ref review . --catalog-only
ref review path/to/App.csproj --refresh --package 'NuGet.*'
ref review path/to/App.slnx --catalog-only --refresh
ref versions path/to/index.json --current 1.2.3
ref processes
ref runtime --pid 12345 --seconds 1 --timeout 30 --modules
ref reflection path/to/library.dll --json
ref reflection path/to/library.dll --fail-on-findings
ref aot inspect path/to/library.dll
ref aot publish path/to/App.csproj --rid osx-arm64 --framework net10.0 --output ./artifacts/aot-audit
```

The default terminal view uses Spectre.Console tables, trees and status panels. ANSI is
disabled for redirected output and `NO_COLOR`; `--plain` gives compact unstyled text.
Add `--json` to any command for structured output; it contains no presentation markup.
`--plain` and `--json` are mutually exclusive. Quote wildcard patterns so the shell
passes them to RefTrace. Package/project library IDs match case-insensitively; recorded
project paths follow the current operating system's case sensitivity. Public type names
match case-sensitively and use metadata names, including `+` for nesting and a backtick
followed by the generic parameter count.

`--seconds` and `--timeout` accept a decimal point in every locale, as well as the local
decimal separator. For example, under `de_AT`, `0.25` and `0,25` both mean a quarter
of a second. Omit thousands separators.

`--framework` accepts an exact recorded target name, a TFM, or a TFM/RID such as
`net10.0/linux-x64`. If an artifact has more than one matching target, selection is required.
The `why` command takes the dependency first and shows one deterministic shortest path,
including the root project. `assets`, `packages`, `why` and `check` accept an optional input:
project file, `.slnx` solution containing one .NET project, directory containing one
project, or explicit assets JSON. Omission means `.`. Directory discovery excludes
build outputs, version-control folders and symlinked
subdirectories. Multiple projects produce `ambiguous-project` with candidate paths;
`review` intentionally includes all discovered projects. MSBuild's evaluated
`ProjectAssetsFile` is honored, and its recorded project identity must match the input.
Missing or mismatched restore evidence requires the caller to restore the project. A
multi-targeted project gets `ProjectAssetsFile` only from the props file that restore writes;
before its first restore RefTrace names `project.assets.json` in `MSBuildProjectExtensionsPath`
and returns `restore-required`.

Explicit `.slnx` input selects only listed `.csproj`, `.fsproj` and `.vbproj` entries;
it does not scan the solution's directory. `review` evaluates all selected projects,
while graph commands require exactly one and report the listed candidate paths
otherwise. Paths resolve relative to the solution file, including entries in logical
solution folders. Both slash styles are accepted, repeated normalized paths are
selected once, and non-.NET projects and solution items are ignored. Malformed XML,
an invalid root, or a project without a Path returns `invalid-solution`; a missing
listed .NET project returns `file-not-found` with its resolved path. An empty solution
or one with no .NET projects returns `project-not-found`. Solutions are never read as
assets JSON, and XML DTDs are not accepted.

`packages` omits asset arrays by default. `--assets` includes compile, analyzer and runtime
paths, `--direct` restricts
the result to direct dependencies, and `--package` filters IDs by a case-insensitive glob.
JSON and plain output retain full values. A filter with no matches produces an empty list.
See [package reviews](PACKAGE-AUDIT.md) for `review` and its saved evidence contract.

`reflection` reads actual managed IL instructions. Its versioned catalog identifies known
reflection, dynamic loading and dynamic-code method references with caller tokens and IL
offsets. Operands are not mistaken for opcodes. It does not prove execution or reachability.
`--fail-on-findings` makes these references an explicit exit-1 policy.
`caller`, `callerToken` and `ilOffset` locate the IL that contains the call. When that IL
is the generated `MoveNext` of an async or iterator state machine, `declaredMethod` and
`declaredMethodToken` name the method the source declared, found through its `AsyncStateMachineAttribute`,
`IteratorStateMachineAttribute` or `AsyncIteratorStateMachineAttribute`. A runtime-async
method (C# 15, `runtime-async=on`) has no state machine: its calls have the method itself as
`caller` and no `declaredMethod`. Lambdas and local functions are reported under their
compiler-generated names only.

`aot inspect` reads compatibility declarations, annotations and static reflection evidence.
Its assessment remains `not-established`, even when compatibility is declared or no known
method reference is found. Annotation data identifies whether arguments were decoded.
ReadyToRun is not treated as Native AOT.

`aot publish` explicitly runs the project's restore/build/publish targets with Native AOT
enabled and warnings as errors. It requires a dedicated output directory and records the
selected SDK version, exact arguments, working directory, RID, UTC interval, exit code,
compiler diagnostics, unique text log and binary log. Each run gets a new subdirectory
under `--output`, so older outputs cannot be mistaken for the latest audit. `--framework`
selects the inner target of a multi-targeted project. The report also contains a labeled
MSBuild property snapshot from evaluation before publish; targets may change values later.
The audit inherits the project's environment and does not rewrite its SDK/feed settings.
It does not push a NuGet package. Project-supplied
suppression/build settings remain in effect. Success applies to that configuration only.

Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Successful inspection, dependency path found, or policy passed |
| 1 | Forbidden dependency found, no path to the requested dependency, or reflection findings with `--fail-on-findings` |
| 2 | Invalid input, unsupported artifact, failed restore, ambiguous target, or inconclusive policy |

For `runtime`, exit 0 additionally requires completed CLR rundown with no reported lost
events and at least one assembly identity. Partial observations are returned with exit 2.
For `aot publish`, exit 0 means the requested publish succeeded, exit 1 means it failed,
and exit 2 means cancellation, timeout or incomplete evidence. Successful static inspection
does not establish AOT compatibility.

## Evidence and limits

* **Assets graphs:** `NuGet.ProjectModel` reads `project.assets.json` formats 3 and 4.
  Nodes preserve resolved versions and selected compile/runtime assets; edges preserve
  requested version constraints. Graphs are scoped to one restored target. The file is a
  snapshot of a restore: RefTrace does not prove that it matches today's project files.
* **Analyzer assets:** NuGet 8.0 (SDK 11.0.100) records a package's analyzer assets in
  each target only when the project sets `RestoreEnableAnalyzerAssets` to `true`. RefTrace
  then lists them per node (`analyzerAssets`) and sets `analyzerAssetsRecorded`. Without the
  property the field is absent and `--assets` says the assets were not recorded; an empty
  list would wrongly claim that a package has no analyzers. Restore records every
  `analyzers/dotnet/roslyn*/` folder of the project's language (Meziantou.Analyzer 3.0.317:
  `roslyn4.8` through `roslyn5.9`). Which folder the compiler loads is decided later by the
  SDK build and is not in the assets file. SDK 11.0.100 recorded them for a `net10.0` target
  as well when the project multi-targeted `net10.0;net11.0`.
* **Missing nodes:** NuGet may retain a dependency constraint for a library excluded from
  a consuming project's target. Such nodes are marked `unresolved`, never silently presented
  as resolved packages. A policy matching one is inconclusive (exit 2). Unmatched unresolved
  nodes remain visible in policy JSON. Missing direct declarations make policy checks
  inconclusive because the root graph cannot be established completely.
* **Assembly manifests:** `PEReader` and `MetadataReader` read identity, MVID, framework,
  file/informational versions, assembly-level metadata keys, reference-assembly markers,
  debug-directory entry kinds and static assembly references. A CodeView entry is debug
  evidence, not a Native AOT verdict. Attribute matching includes namespace and assembly scope.
  Public/protected declared signatures are available with `--type`. Each member includes
  kind, accessibility, static status, value/return type, parameter names/types/ref-kinds,
  optional/default constants, generic constraints, visible accessors and a readable
  `signature`. Types include kind, base type, interfaces and generic constraints.
  Constructors, fields and events are included; property/event accessors are not repeated
  as methods. This is metadata evidence, not a binary/source compatibility verdict.
  Inherited members, nullable annotations and other custom-attribute semantics are not
  expanded. Names use metadata generic arity; type names do not encode assembly scope.
* **C# 15 and .NET 11 metadata:** checked against assemblies compiled by SDK 11.0.100
  (`tests/RefTrace.LanguageFixture`).
  * Types carry `modifiers` (`abstract`, `sealed`, `static`, `closed`, `readonly`, `ref`) and
    `layout` (`auto`, `sequential`, `explicit`, or `extended CStruct`/`extended CUnion`).
  * A `closed` class is abstract in metadata and carries `IsClosedTypeAttribute`, whose
    `DerivedTypes` lists the direct subtypes; RefTrace reports them as `closedDerivedTypes`.
    Closure is not transitive, and metadata records nothing on the subtypes themselves.
  * A type with `UnionAttribute` reports `unionCases`, the types the compiler treats as its
    cases: the parameters of the static `Create` methods of a nested `IUnionMembers` provider
    when there is one, otherwise of its public single-parameter constructors. A case taken
    by `in` reference is reported as its value type. A `union` declaration and a
    hand-written `[Union]` type are reported alike; metadata does not record which one the
    source used.
  * Members of a C# 14 extension block appear on the static class with a `receiver` and an
    `extension(...)` prefix in `signature`, including extension properties and indexers. A
    generic block reports its type parameters and constraints in `extensionGenericParameters`
    and in the prefix, as in `extension<T>(List`1<T> list) where T : class`; they come from
    the marker type, because the grouping type uses compiler names such as `$T0`. The
    compiler's grouping and marker types (`<G>$…`, `<M>$…`) are not listed. The static
    implementation methods that compiled callers bind to, such as `get_IsEmpty(Buffer)`,
    remain listed as methods. A grouping member without a resolvable marker is shown with
    `extension(receiver not recorded)`.
  * An extended-layout type gets a note when .NET 11.0.0 refuses to load it (observed as
    `TypeLoadException`): it is a ref struct, or an instance field is a reference, a byref,
    or a value type with auto layout, directly or inside a value type of the same assembly.
    The note follows the path to the offending field. A field whose type is a value type from
    another assembly (`DateTime` has auto layout, `Guid` does not), a generic value type or a
    type parameter without an `unmanaged` constraint is reported as undecidable from this
    assembly's metadata. Enums are accepted.
  * `memorySafetyRulesVersion` comes from the module's `MemorySafetyRulesAttribute` (2 under
    C# 15's updated rules). Under version 2 each member reports `requiresUnsafe`, true when it
    carries `RequiresUnsafeAttribute`, and its signature shows `unsafe`. Without module rules
    `requiresUnsafe` is absent: an unmarked member then says nothing about safety. .NET
    11.0.0's reference assemblies use version 2 and mark, for example, `Marshal.Copy`; the
    implementation in `System.Private.CoreLib` records no rules and no marker.
  * A runtime-async method and a state-machine async method have the same declared
    signature. Metadata records the difference (`MethodImplAttributes.Async`, `0x2000`,
    versus `AsyncStateMachineAttribute`), but it is implementation, not API, and `assembly`
    does not print it.
  * A method that is virtual and final in metadata, such as an implicit interface
    implementation, is not printed as `virtual`.
* **Runtime observation:** `processes` lists processes advertising .NET diagnostics.
  `runtime` attaches to an explicitly chosen PID, observes loader events for `--seconds`,
  then requests CLR rundown so previously loaded assemblies can also be reported.
  The report includes target and observer PIDs, UTC interval, assembly identities,
  module paths, load/unload evidence, rundown status and event loss. This is an observed
  interval, not an atomic memory snapshot. It does not infer AssemblyLoadContext names
  or a dependency graph between the observed assemblies.
* **Diagnostics availability:** live observation needs an accessible diagnostic port and
  compatible EventPipe protocol. It has been exercised with .NET 10 and .NET 11 CoreCLR.
  Native AOT processes, disabled diagnostics, permissions and container boundaries can
  prevent observation. An unavailable endpoint is not evidence that no assemblies exist.
  Collection enables loader events and can affect the target's timing; it does not inject
  the observer's Spectre.Console/diagnostics dependencies into that process.
* **SDK files:** `sdk` locates the SDK selected in the supplied project/directory context
  using `dotnet --version` and `dotnet --list-sdks`, or reads an explicit SDK installation
  directory. It reports known native CLI
  payload files and the bundled Roslyn compiler's assembly/informational version.
  Presence does not prove activation for a particular invocation or Rider's toolset choice.
* **Artifact limits:** neither static artifact proves dynamic loads, the SDK that performed
  a historical build, the runtime binding of every reference, or the target of dynamically
  compiled source. Embedded DLLs are not automatically unpacked.

Binary logs remain local. `ProjectImports=None` avoids embedding imported project files,
but binary logs can still contain paths and build configuration; this does not redact all
sensitive values. RefTrace does not enable logging every environment variable or upload logs.

These sources are deliberately kept distinct. A NuGet package version, assembly identity
version, informational version and target framework are different pieces of evidence.

## JSON contract

Every structured report has an envelope:

```json
{
  "schemaVersion": 2,
  "kind": "assets",
  "data": {}
}
```

Kinds are `assets`, `packages`, `why`, `check`, `assembly`, `sdk`, `processes`, `runtime`,
`versions`, `review`, `reflection`, `aot-inspect`, `aot-publish`, and `error`. Consumers should check
`schemaVersion` and tolerate additional fields. Removing fields or changing their meaning
requires a new schema version. Error data has `code` and `message`; diagnostic text is not
a stable machine-readable identifier. Optional `input` identifies the input that failed;
`candidates` contains valid project, framework or SDK choices. Codes include
`invalid-argument`, `file-not-found`, `path-not-found`, `directory-not-found`,
`project-not-found`, `invalid-solution`, `ambiguous-project`, `ambiguous-target`, `target-not-found`,
`restore-required`, `mismatched-assets`, `restore-failed`, `evaluation-failed`,
`sdk-unavailable`, `ambiguous-sdk`, `tool-unavailable`, `output-exists`, `review-failed`,
`invalid-artifact`, `unsupported-artifact`, `inconclusive-policy`, `diagnostics-unavailable`,
`access-denied`, `io-error`, `timeout` and `cancelled`. Absolute paths are machine-specific.

Schema 2 removes the old compact member-name records, omits unrequested package asset
arrays, and replaces the generic `invalid-input` code with specific errors. JSON is
written on one line. The 0.x versions make no schema-1 compatibility
promise. A review with per-package failures still returns its `review` report and exit 1;
its detailed saved `report.json` has its own schema version 2.

## Develop and test

From the repository root, run `make check` with the pinned .NET SDK, Make, and uv.
It restores and builds the Release solution, runs the .NET and Python tooling tests,
and runs the version of JetBrains InspectCode pinned in `dotnet-tools.json`.
`make inspect` runs the inspection gate on its own. It prints findings with their
locations and saves the complete report, including suggestions, to
`artifacts/inspectcode.sarif`. Linux CI runs the same inspection gate.

```sh
dotnet build RefTrace.slnx -c Release
dotnet test --solution RefTrace.slnx -c Release --report-trx
dotnet run --project src/RefTrace.Cli -- --help
```

Layout:

* `RefTrace.Model`: typed reports, graph queries and policies; no package dependencies.
* `RefTrace.Metadata`: PE/IL readers, SDK evidence and static reflection/AOT inspection.
* `RefTrace.Assets`: NuGet restore-file adapter.
* `RefTrace.Runtime`: EventPipe observation and runtime event aggregation.
* `RefTrace.Build`: project/SDK selection, bounded process capture, publish execution and compiler evidence.
* `RefTrace.Cli`: command parsing, Spectre.Console presentation and exit-code mapping.
* `RefTrace.Tests`: reader, graph, policy, metadata and CLI tests using MSTest with native MTP.
* `RefTrace.RuntimeFixture`: a controlled child process for live-observation integration tests.
* `RefTrace.AnalyzerAssetsFixture`: a project that restores with `RestoreEnableAnalyzerAssets`.
* `RefTrace.LanguageFixture`: C# 15 and .NET 11 constructs whose metadata the tests read.

Keep artifact-format adaptation in the readers. Keep graph policy independent of files and
the CLI. The core reads streams as well as paths so new formats and tests do not require
mocking a process or an SDK installation. Package versions are in `Directory.Packages.props`.
See [ARCHITECTURE.md](../ARCHITECTURE.md) for dependency and evidence boundaries.

## Native AOT and dependencies

The published tool currently runs on CoreCLR. Native AOT tool packaging is supported by
.NET SDK 10 and later, but that does not make every tool dependency AOT-compatible.
An `osx-arm64` Native AOT publish audit of the tool reported trimming/AOT errors
in TraceEvent and Newtonsoft.Json. On SDK 11.0.100, `ref aot publish` of the CLI project
stops earlier, with IL3000 for the two `Assembly.Location` reads in `ReviewCommand.cs` that
find the bundled review collector. None of these errors is suppressed, and `PublishAot` is
not enabled for the shipped package. The CLI's own JSON output uses source generation.

The isolated Model and Metadata modules enable AOT compatibility analysis. A metadata-only
fixture was also published as Native AOT on macOS ARM64, then its native executable was
used to analyze a managed DLL. That result is separate from full-CLI compatibility.

Spectre.Console belongs only to the presentation project. A graph of RefTrace's own
dependencies correctly includes it; that is not contamination of an inspected project
or process. Runtime diagnostics dependencies are isolated in `RefTrace.Runtime`.

## Relationship to GitHub security features

[GitHub's dependency graph](https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-graph)
aggregates manifests, lock files and submitted dependency snapshots. RefTrace's restored
target graph is local evidence and is not automatically uploaded to GitHub.
[Dependency submission](https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-graph-data)
would be an explicit export/integration step.
[Code scanning](https://docs.github.com/en/code-security/concepts/code-scanning/code-scanning)
finds source-code problems using tools such as CodeQL or SARIF-producing analyzers.
RefTrace is not a vulnerability scanner; a passed dependency policy is not a security verdict.

## Package and try locally

```sh
dotnet pack src/RefTrace.Cli/RefTrace.Cli.csproj -c Release -o artifacts/packages
dotnet tool install RefTrace --version 0.11.1 --source ./artifacts/packages --tool-path ./artifacts/tools
./artifacts/tools/ref --help
```

The version comes from `Directory.Build.props`; use a new one for each release. Local packaging does not reserve
the package ID. A release runs through the manually started workflow that [Publishing](PUBLISHING.md) describes.
`CHANGELOG.md` tracks behavior changes; package SemVer and `schemaVersion` are maintained independently.
