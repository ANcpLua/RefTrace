# Changelog

## 0.12.0

* Add `ref update` and `ref --update` to update the running .NET tool installation. See
  `ref help update` for the feed option and the technical reference for Windows completion receipts.
* Preserve the outer parameter's by-reference kind, including `ref readonly`, custom modifiers and
  function-pointer parameters whose own return type is by reference. Escape an apostrophe in character
  defaults and constants.
* Filter union case reports by the signatures of the provider's eligible factory methods.

## 0.11.1

* Fractional durations accept a decimal point in every locale and the local decimal separator.
  Under `de_AT`, both `0.25` and `0,25` work for `ref runtime --seconds`, `ref runtime --timeout`
  and `ref aot publish --timeout`.
* `make check` restores and builds the solution before running InspectCode. The report includes
  suggestions, and warnings or errors fail the gate.

## 0.11.0

* Add `ref version`. It prints the same line as `ref --version`, the package version and the commit it was built
  from, by running that System.CommandLine option.

## 0.10.0

* The version carries no preview suffix any more. The suffix dated from the time on .NET 11 release candidates;
  RefTrace now builds and runs on the released .NET 11 SDK. `dotnet tool install --global RefTrace` finds it
  without `--prerelease`.
* Target NuGet 8.0: NuGet.ProjectModel 8.0.0 replaces 7.9.0, and all nine NuGet packages resolve to 8.0.0.
* List the analyzer assets that restore records with `RestoreEnableAnalyzerAssets` in `packages --assets` and
  graph JSON, and say when a restore did not record them.
* Report review candidates younger than the minimum publish age configured for nuget.org, with the matching
  exception pattern, through NuGet.Configuration 8.0.
* Describe C# 15 and .NET 11 types in `assembly`: closed classes and their direct subtypes, union cases,
  extension members with their receiver instead of compiler scaffolding, extended layout with a note for
  reference fields the runtime rejects, memory-safety rules and members that require an unsafe context.
* Report type modifiers and layout, and stop printing final methods, such as interface implementations, as
  `virtual`. `signature` text therefore changes for existing assemblies, as it does for extension members
  (`extension(...)` prefix) and members that require an unsafe context (`unsafe`); compare API reports made
  by the same RefTrace version.
* Name the declared method for reflection calls inside async and iterator state machines.
* Save review evidence under `artifacts/.package-audit/` instead of `artifacts/package-audit/`. The decompiled and
  upstream `.cs` files of the old folder became part of a project in the same directory and broke its next build;
  the .NET SDK leaves folders whose name starts with a dot out of a project's default items. Delete an old
  `artifacts/package-audit/` folder that lies below a project.
* Return `restore-required` for a multi-targeted project that was never restored; it was `evaluation-failed`.
* Compile RefTrace with C# 15 runtime async.
* Stop `global.json` from selecting prerelease SDKs.
* Run the AOT publish tree-kill test in parallel again; .NET 11.0.0 fixes the RC3 macOS reaping stall.
* Recheck the transitive pins and the async guide on SDK 11.0.100, the 11.0.0 runtime and NuGet 8.0.

## 0.9.0-preview.1

* Accept explicit .slnx solutions for package reviews and project input resolution.
* Review only the listed .NET projects, excluding unrelated prototypes and local examples.
* Report malformed solutions and missing project paths with actionable diagnostics.
* Target the .NET 11 release: SDK 11.0.100, System.CommandLine and Microsoft.DiaSymReader 3.0.0, and the 11.0.0
  releases of Pkcs, ProtectedData and DependencyModel replace their RC3 builds.

## 0.8.1-preview.1

* Remove optional MSTest usage telemetry and its Application Insights dependency at restore time.
* Check the test dependency graph with the packaged `ref` command in CI to prevent telemetry returning unnoticed.
* Keep native test execution, TRX reporting and code coverage enabled.

## 0.8.0-preview.1

* Upgrade five reviewed transitive dependencies through central package pinning.
* Retain Application Insights 2.23.0 after reproducing the test runner's missing-method failure with 3.2.0.
* Exercise NuGet JSON writers and Windows credential encryption in compatibility tests.
* Collect coverage in all three CI environments to exercise the upgraded coverage dependencies.
* Use C# 15 collection arguments while preserving case-insensitive directory exclusions.
* Run process-tree cleanup tests separately to avoid the observed RC3 macOS reaping stall.

## 0.7.0-preview.1

* Review RefTrace with its own installed command and record all 44 async/API decisions.
* Upgrade MSTest.Sdk to 4.5.2, including its MSTest analyzers and MTP 2.5.2 dependencies.
* Remove the Core type-forwarding assembly; reference the owning modules directly.
* Remove the synchronous CLI entry point and await commands throughout the CLI tests.
* Consolidate assembly-attribute name decoding in the existing metadata reader.
* Observe EventPipe reader failures throughout bounded cancellation cleanup.
* Cancel and join AOT output reads when inherited pipes remain open after process exit.

## 0.6.0-preview.1

* Accept project or directory inputs and resolve custom restore paths with MSBuild.
* Use `ref why <dependency> [input]`; select the current directory's SDK with `ref sdk`.
* Ship `ref review` with project/repository inventory, release filtering and saved evidence.
* Default to compact package summaries; add `--direct`, `--package` and `--assets`.
* Decode declared API signatures including overloads, generics, parameters and accessors.
* Adopt JSON schema 2 with specific error codes, input details and candidate choices.
* Include added/removed assemblies and same-name signature changes in package comparisons.

## 0.5.0-preview.1

* Target .NET 11 RC3 and use newer System.CommandLine, Pkcs and Spectre.Console previews.
* Add NuGet-native version comparisons and an explicit C# 15 release-decision union.
* Add repeatable uv/Make/mise package audits with hashes, pinned source probes and ILSpy comparisons.
* Use the library help renderer for command details while retaining the short overview.

## 0.4.0

* Shorten the command to `ref`, add `ref help`, and make the help overview concise.
* Clarify installation and update the SDK to .NET 10.0.403.
* Address InspectCode findings while preserving metadata fixtures and public C# names.
* Remove the premature public API freeze during initial development.

## 0.3.0

* Separate Model, Metadata, Assets and Build modules; retain Core type forwarders.
* Add reflection/dynamic-code method-reference inspection with IL offsets and a versioned catalog.
* Add static AOT declaration/annotation inspection without claiming compatibility.
* Add explicit Native AOT publish audits with SDK, command, outcome, log and compiler diagnostics.
* Report forwarded types and document architecture/evidence boundaries.
* Include assembly-level metadata and debug directory kinds, with scope/namespace checks.
* Record pre-publish MSBuild properties and a unique binary log in isolated per-run audit folders.
* Add publishing gates for the accepted commit and cross-platform CI.

## 0.2.0

* Add package inventory with direct/transitive origin, SDK file inspection and diagnostic process discovery.
* Add live EventPipe loader/rundown observations with explicit completion and event-loss status.
* Add Spectre.Console tables, trees and panels, with independent plain-text and JSON output modes.
* Use source-generated JSON while retaining schema version 1 and existing report shapes.
* Document Native AOT audit limits and distinctions from GitHub dependency graphs/code scanning.

## 0.1.0

* Read NuGet restore graphs for one target, including compile/runtime asset selection.
* Explain dependency paths and check forbidden package/project dependencies.
* Inspect static assembly identities, version attributes, references and public type/member names.
* Add schema-versioned JSON output and documented exit codes.
* Package the `reftrace` command as a .NET tool.
