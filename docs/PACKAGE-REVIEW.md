# Preview package review — 2026-10-26

This is the historical migration record. See the [2026-10-29 self-review](SELF-REVIEW.md)
for the current package decisions and validation.

The application now targets .NET 11 with SDK `11.0.100-rc.3.26522.148`.
The initial collection inspected 33 resolved package coordinates and 24 package archives.
The follow-up collection also covered 33 coordinates. Both completed without collection
failures. Full records, source files, DLL reports and decompilation diffs are kept in
`artifacts/package-audit/` with timestamped run directories.

## Direct dependencies

| Package | Before | Selected | Decision |
| --- | --- | --- | --- |
| Microsoft.Diagnostics.NETCore.Client | 0.2.750501 | 0.2.750501 | No newer prerelease; retained current release |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | 4.0.0 | No newer prerelease; retained current release |
| MSTest.Sdk | 4.5.1 | 4.5.1 | No newer prerelease; retained current release |
| NuGet.ProjectModel | 7.9.0 | 7.9.0 | No newer prerelease; retained current release |
| Spectre.Console | 0.57.2 | 0.57.3-alpha.0.29 | Newer listed prerelease; upgraded |
| System.CommandLine | 2.0.14 | 3.0.0-rc.3.26522.148 | Newer listed prerelease; upgraded |
| System.Security.Cryptography.Pkcs | 10.0.14 | 11.0.0-rc.3.26522.148 | Newer listed prerelease; upgraded |

The System.CommandLine DLL and its exact source revision agree on the new `HelpName`
property. RefTrace uses it and delegates detailed help to the library. Its `HelpBuilder`
remains internal. Spectre's inspected `AnsiConsole` decompilation did not change between
these two package versions. Pkcs's inspected `SignedCms` difference includes ASN.1
implementation changes; this is not a claim that its whole package is API-identical.

## Remaining transitive candidates

These were recorded, not injected as direct references. They belong to the dependency
closure of the selected tooling and libraries.

| Package | Resolved | Newer candidate |
| --- | --- | --- |
| Microsoft.ApplicationInsights | 2.23.0 | 3.2.0 |
| Microsoft.DiaSymReader | 2.2.10 | 3.0.0-rc.3.26522.148 |
| Microsoft.Extensions.DependencyModel | 10.0.10 | 11.0.0-rc.3.26522.148 |
| Microsoft.TestPlatform.ObjectModel | 18.10.1 | 18.11.0 |
| Newtonsoft.Json | 13.0.3 | 14.0.1-beta2 |
| System.Security.Cryptography.ProtectedData | 8.0.0 | 11.0.0-rc.3.26522.148 |

## Limits

The comparison distinguishes package versions, static assembly references, public names,
selected decompiled types and original source probes. It does not equate these with the
assemblies a process loads. Hash verification checks the NuGet catalog's bytes, not a
reproducible source build or general compatibility. The application tests and packaged
command are verified separately.

Run `make audit` for a new observation. This document records the decision for this
migration; it is not a claim about the latest versions at a later date.
