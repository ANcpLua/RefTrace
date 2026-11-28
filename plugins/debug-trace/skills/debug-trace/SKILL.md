---
name: debug-trace
description: Debug wrong results, crashes, hangs, failing tests, and misleading or missing diagnostics. Trace execution, requests, unexpected loads, or dependencies in any language, including problems found mid-task. Use RefTrace for NuGet, managed assemblies, and CLR loader events.
---

# Debug and trace

In a mixed repository, follow the failing test, frame, request, or dependency to its owning project or process.

- **Code paths and failures.** For a source walkthrough, follow calls from the specified entry point. For an observed failure, inspect the reproducer and stack with the runtime's debugger, such as Python's `pdb`, the Node inspector, LLDB for native code, or the managed debugger for .NET.
- **Misleading or missing diagnostics.** Run the affected checker on a valid input that should report and a neighboring input that should stay silent. Compare the diagnostic ID, count, and location before and after the fix. When equivalent source spellings give different results, compile them with the same settings and inspect the bound symbol, operation, and source span. Check a claim about runtime behavior by varying the input or setting it depends on.
- **Requests crossing services.** Follow the request's trace ID through existing spans and logs. Compare parent and child spans to locate the failing or slow operation.
- **Dependency origin.** Use the package manager that resolved the component, such as `npm explain <package>` for an installed npm dependency. Use RefTrace for a NuGet restore graph.

## RefTrace

The NuGet tool `RefTrace` runs on .NET 11. Invoke it as `ref`, or use `dotnet tool run ref -- …` when the repository pins it in a tool manifest. Check `version` through that same installation when diagnosing version-dependent behavior. If RefTrace is unavailable, continue with the project's existing diagnostics.

When asked to upgrade RefTrace 0.12.0 or later, use `update` through the same command prefix. An older copy needs `dotnet tool update` with its original installation scope. For a Windows shim reporting `scheduled`, read the reported completion file for the SDK outcome before confirming the new version.

| Question | Command | Evidence |
| --- | --- | --- |
| Why is this NuGet package here? | `ref why <package> <project> --json` | One shortest path in the last restore. Use `ref packages <project> --json` for the resolved inventory. |
| Which dependencies violate a project boundary? | `ref check <project> --forbid-package 'Legacy.*' --forbid-project '*.Tests.csproj' --json` | Repeat either option to check all patterns against the same restore. Each violation records the matching pattern and the shortest path when reachable. |
| Which API or reflection call is in this DLL? | `ref assembly <dll> --type 'Namespace.Type' --json` or `ref reflection <dll> --json` | Declared signatures or static IL call sites in a managed assembly. |
| Which assembly did this process load? | `ref processes --json`, then `ref runtime --pid <pid> --seconds 1 --json` | CLR loader events and rundown for the selected PID. This also works for a foreign host that exposes compatible .NET diagnostics. |
| Which SDK was selected? | `ref sdk <directory> --json` | The .NET SDK selected in that directory. This can succeed in a pure Node or Python project. |
| Will Native AOT publish succeed? | `ref aot inspect <dll> --json` for static findings. | Static inspection reports `not-established`. To test the requested configuration, `ref aot publish <app.csproj> --rid <rid> --output <dir> --json` runs a real publish. |

For a missing .NET analyzer diagnostic, run `ref packages <project> --package '<package>' --assets --json`. If `data.analyzerAssetsRecorded` is false, refresh the evidence with `dotnet restore <project> --force -p:RestoreEnableAnalyzerAssets=true` and repeat the query. When it is true, `analyzerAssets` lists the paths NuGet recorded for that package and target. Check the `Csc` task's `Analyzers` in the failing build's MSBuild binary log to see which DLLs reached the compiler.

For graph queries, pass the relevant `.csproj`, `.fsproj`, `.vbproj`, or restored `project.assets.json`. On `ambiguous-target`, select the intended target from `data.candidates` with `--framework`. On `restore-required`, restore the selected project if current dependency evidence is needed, then retry.

Read the exit status alongside `schemaVersion`, `kind`, and `data`. In schema 2, exit 1 has a command-specific meaning, such as an absent path for `why`. Exit 2 is inconclusive, even when a partial runtime report contains assemblies. `diagnostics-unavailable` means the endpoint could not be used. Report the artifact, target, or observation window that supports the finding.

## Finish an investigation

Keep disposable reproducers and captures in a task-owned directory. Once the results have been read, remove those probes and this run's temporary logs, dumps, reports, and build outputs before committing or pushing. Preserve requested deliverables, regression tests, and pre-existing files in shared checkouts.
