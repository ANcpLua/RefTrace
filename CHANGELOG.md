# Changelog

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
