---
name: package-evidence
description: Compare NuGet releases with installed versions, package bytes, source revisions and DLL APIs using RefTrace and ILSpy. Use for preview upgrades or conflicting package/documentation evidence in this repository.
---

# Package evidence

Read the repository AGENTS.md and current manifests first. Preserve existing edits.
The durable implementation is `tooling/reftrace_audit`, run in the project's uv
environment. Make orchestrates; mise tasks call Make. Do not install Python packages
into a global interpreter or change the host's SDK selection for another project.

## Collect

1. `make audit-test` validates the Python collector. `make build` provides the local
   `ref` executable and current restore graphs.
2. `make audit` collects direct package pins, MSBuild SDK pins and the resolved package
   coordinates from every project's assets graph. `make audit-catalog` only checks versions.
3. Use `uv run --frozen python -m tooling.reftrace_audit --root . --refresh` for fresh
   remote evidence. `--offline` uses cached inputs only and fails on missing inputs.
   `--output` must name a new directory. Never overwrite an earlier report.
4. Read the reported `report.json`, including failures, source receipts and limitations.
   Individual package directories contain the exact source probes, metadata reports
   and ILSpy type output. A zero exit code means collection completed, not that all
   candidates are suitable upgrades.

## Decide

- NuGet V3 endpoints come from its service index. The flat-container index includes
  unlisted releases; candidates must also pass the registration listing check.
- `ref versions` delegates comparison to NuGet.Versioning. Prefer a newer preview/RC
  when it is actually newer; never downgrade a current stable release to a historical
  preview merely because the project uses previews.
- The package SHA-512 is checked against the catalog value. This establishes matching
  feed bytes, not publisher identity or reproducible builds.
- A nuspec repository commit is a provenance claim. Retrieved files are pinned to it.
  A configured upstream fallback is labelled separately. Missing source stays missing.
- Metadata reports list names and references. Name diffs do not compare full overload
  signatures. ILSpy diffs include implementation and formatting; assess them with the
  original source, then build and run behavior tests after changing the dependency.
- New versions of transitive dependencies are review findings. Upgrade their owning
  direct dependency first; do not add override references without a concrete reason.
- Expand `tooling/audit-probes.json` for APIs the application actually calls. Do not
  assume that an unprobed part of a package is unchanged.

## Change and verify

Update direct versions centrally, restore/build, run `make test audit-test`, and inspect
the packaged tool. Refresh third-party notices for the actual shipped runtime assets.
Keep JSON schema changes, package versions and catalog versions separate.

For C# 15 unions, use an exhaustive switch expression on the union, with an explicit
null/default arm. The existing `ReleaseDecision` is the local example. Do not use a
catch-all arm to silence a newly added case, or add legacy polyfills to net11.0.

Keep source/API findings and application verification separate in the handoff.
Commit and push only within the user's authorized scope; do not trigger publication
as a side effect of collecting or upgrading packages.
