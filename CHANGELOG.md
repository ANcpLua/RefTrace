# Changelog

## 0.3.0

* Separate Model, Metadata, Assets and Build modules; retain Core type forwarders.
* Add reflection/dynamic-code method-reference inspection with IL offsets and a versioned catalog.
* Add static AOT declaration/annotation inspection without claiming compatibility.
* Add explicit Native AOT publish audits with SDK, command, outcome, log and compiler diagnostics.
* Report forwarded types and document architecture/evidence boundaries.
* Include assembly-level metadata and debug directory kinds, with scope/namespace checks.
* Record pre-publish MSBuild properties and a unique binary log in isolated per-run audit folders.

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
