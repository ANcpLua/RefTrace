# Technical reference

This document covers report details, limitations, build setup, and packaging.
Start with the [README](../README.md) for everyday commands.

Run the development commands below from the RefTrace repository root.

Trace resolved .NET dependencies and inspect assembly metadata from the command line.
RefTrace reads existing artifacts and can explicitly observe a live .NET process.
Artifact inspection does not restore projects, load target assemblies, or invoke their code.
Live observation is a separate command using the target's diagnostics endpoint.

Requires the .NET 10 runtime. Development uses the SDK pinned in `global.json`.

## Commands

```sh
reftrace assets path/to/project.assets.json --framework net10.0
reftrace packages path/to/project.assets.json --framework net10.0
reftrace why path/to/project.assets.json System.Security.Cryptography.Pkcs --framework net10.0
reftrace check path/to/project.assets.json --forbid-package 'Basic.Reference.Assemblies.*'
reftrace check path/to/project.assets.json --forbid-project '*Other.Tests.csproj'
reftrace assembly path/to/library.dll
reftrace assembly path/to/library.dll --type 'Some.Namespace.Outer+Nested'
reftrace sdk /usr/local/share/dotnet/sdk/11.0.100-rc.2.26504.105
reftrace processes
reftrace runtime --pid 12345 --seconds 1 --timeout 30 --modules
reftrace reflection path/to/library.dll --json
reftrace reflection path/to/library.dll --fail-on-findings
reftrace aot inspect path/to/library.dll
reftrace aot publish path/to/App.csproj --rid osx-arm64 --framework net10.0 --output ./artifacts/aot-audit
```

The default terminal view uses Spectre.Console tables, trees and status panels. ANSI is
disabled for redirected output and `NO_COLOR`; `--plain` gives compact unstyled text.
Add `--json` to any command for structured output; it contains no presentation markup.
`--plain` and `--json` are mutually exclusive. Quote wildcard patterns so the shell
passes them to RefTrace. Package/project library IDs match case-insensitively; recorded
project paths follow the current operating system's case sensitivity. Public type names
match case-sensitively and use metadata names, including `+` for nesting and a backtick
followed by the generic parameter count.

`--framework` accepts an exact recorded target name, a TFM, or a TFM/RID such as
`net10.0/linux-x64`. If an artifact has more than one matching target, selection is required.
The `why` command shows one deterministic shortest path, including the root project.

`reflection` reads actual managed IL instructions. Its versioned catalog identifies known
reflection, dynamic loading and dynamic-code method references with caller tokens and IL
offsets. Operands are not mistaken for opcodes. It does not prove execution or reachability.
`--fail-on-findings` makes these references an explicit exit-1 policy.

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
| 1 | Forbidden dependency found, or no path to the requested dependency |
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
* **Missing nodes:** NuGet may retain a dependency constraint for a library excluded from
  a consuming project's target. Such nodes are marked `unresolved`, never silently presented
  as resolved packages. A policy matching one is inconclusive (exit 2). Unmatched unresolved
  nodes remain visible in policy JSON. Missing direct declarations make policy checks
  inconclusive because the root graph cannot be established completely.
* **Assembly manifests:** `PEReader` and `MetadataReader` read identity, MVID, framework,
  file/informational versions, assembly-level metadata keys, reference-assembly markers,
  debug-directory entry kinds and static assembly references. A CodeView entry is debug
  evidence, not a Native AOT verdict. Attribute matching includes namespace and assembly scope.
  Public/protected declared member names are available with `--type`. This is not a full
  signature/API compatibility comparison or an IL decompiler.
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
* **SDK files:** `sdk` reads the supplied SDK directory and reports known native CLI
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
  "schemaVersion": 1,
  "kind": "assets",
  "data": {}
}
```

Kinds are `assets`, `packages`, `why`, `check`, `assembly`, `sdk`, `processes`, `runtime`,
`reflection`, `aot-inspect`, `aot-publish`, and `error`. Consumers should check
`schemaVersion` and tolerate additional fields. Removing fields or changing their meaning
requires a new schema version. Error data has `code` and `message`; diagnostic text is not
a stable machine-readable identifier. Absolute evidence paths are machine-specific.

## Develop and test

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
* `RefTrace.Build`: explicit publish execution and compiler evidence.
* `RefTrace.Cli`: command parsing, Spectre.Console presentation and exit-code mapping.
* `RefTrace.Core`: compatibility facade forwarding original public type names.
* `RefTrace.Tests`: reader, graph, policy, metadata and CLI tests using MSTest with native MTP.
* `RefTrace.RuntimeFixture`: a controlled child process for live-observation integration tests.

Keep artifact-format adaptation in the readers. Keep graph policy independent of files and
the CLI. The core reads streams as well as paths so new formats and tests do not require
mocking a process or an SDK installation. Package versions are in `Directory.Packages.props`.
See [ARCHITECTURE.md](../ARCHITECTURE.md) for dependency and evidence boundaries.

## Native AOT and dependencies

The published tool currently runs on CoreCLR. Native AOT tool packaging is supported by
.NET SDK 10 and later, but that does not make every tool dependency AOT-compatible.
An `osx-arm64` Native AOT publish audit of the current tool reported trimming/AOT errors
in TraceEvent and Newtonsoft.Json. Those errors are not suppressed and `PublishAot` is
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

For the prepared GitHub Actions workflow and NuGet account setup, see [Publishing setup](PUBLISHING.md).

```sh
dotnet pack src/RefTrace.Cli/RefTrace.Cli.csproj -c Release -o artifacts/packages -p:Version=0.3.0
dotnet tool install RefTrace --version 0.3.0 --add-source ./artifacts/packages --tool-path ./artifacts/tools
./artifacts/tools/reftrace --help
```

Use a new package version for each release. Before a public release, set the chosen license
and repository metadata in the package project, run the tests, and test the packed tool.
Publishing requires ownership of the NuGet package ID and your NuGet publishing credentials
or configured trusted publishing. Local packaging does not reserve the ID.

```sh
dotnet nuget push artifacts/packages/RefTrace.0.3.0.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

The project has no automatic publish step. `CHANGELOG.md` tracks behavior changes; package
SemVer and `schemaVersion` are maintained independently.
