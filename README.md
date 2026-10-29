# RefTrace

Find where your .NET dependencies come from and what your app loads.

RefTrace helps you follow a package back to its source, look inside a DLL, and check
a running app. It shows what it found and tells you when there is not enough information
to draw a conclusion.

Private preview development on .NET 11 RC3.

## Install

The first NuGet release is pending. Install the current source with the .NET 11 RC3 SDK:

```sh
dotnet pack src/RefTrace.Cli -c Release -o artifacts/packages
dotnet tool install --global RefTrace --version 0.8.1-preview.1 --source artifacts/packages
ref help
```

After publication, install with `dotnet tool install --global RefTrace`.
The package is named **RefTrace**; the command is **ref**.
For an existing installation, replace `install` with `update`.

`ref help` gives a short overview. Use `ref help assembly` or `ref assembly --help`
for a command's arguments and options.

## Find a dependency

```sh
ref packages MyApp.csproj
ref packages MyApp.csproj --direct
ref why Some.Package MyApp.csproj
ref check MyApp.csproj --forbid-package 'Unwanted.*'
```

Inputs can be a project, a directory containing one project, or a restored assets file.
Leave the input out to use the current directory. RefTrace asks MSBuild where that
project keeps its assets; it does not restore or build it. Ambiguous selections return
the available choices. Use `--framework net11.0` for a specific restored target.

Package output shows IDs, versions, and direct/transitive origin. Use `--package
'NuGet.*'` to filter it and `--assets` to include compile/runtime paths.

Use `assets` for the full graph, including project references. To check a project boundary,
use `--forbid-project '*Other.Tests.csproj'`. Quote patterns so your shell passes them through.

## Review package updates

```sh
ref review . --catalog-only
ref review MyApp.csproj --package 'NuGet.*' --refresh
ref review MyApp.csproj --offline --json
```

`review` accepts one project or a repository containing several. It checks resolved
versions against listed NuGet releases and saves a report under `artifacts/package-audit/`.
A full review also collects package bytes, declared API signatures, and configured
source/implementation probes. It reports candidates without editing dependency pins.
The collector ships with `ref`; it needs uv and Python 3.14, plus `ilspycmd` for configured
decompilation probes. See [package reviews](docs/PACKAGE-AUDIT.md) for details.

## Look inside a DLL

```sh
ref assembly MyLibrary.dll
ref assembly MyLibrary.dll --type 'MyNamespace.MyType'
ref reflection MyLibrary.dll
```

`assembly` shows versions, references, and other recorded details. With `--type`, it
also reports declared public/protected signatures, including overload parameters,
return types, generics, constructors, fields, events, and property accessors. `reflection` shows
known reflection and dynamic-code calls worth reviewing. A call found in a DLL does not
mean it ran. Neither command executes code from the DLL.

## Watch a running app

```sh
ref processes
ref runtime --pid 12345 --seconds 1 --modules
```

Choose a PID from the first command. The second shows assemblies observed in that process
and their file paths. It reports the time window and whether collection completed.
It cannot tell you what the app will load later.

## Check Native AOT

```sh
ref aot inspect MyLibrary.dll
ref aot publish MyApp.csproj --rid osx-arm64 --output artifacts/aot-check
```

`inspect` gives you things to review. `publish` tries a real Native AOT build and saves
its result and logs in a new folder. It runs the project's build steps; it does not upload
anything to NuGet. For a multi-targeted project, add `--framework net11.0`.

To see which compiler and native CLI files an installed SDK contains:

```sh
ref sdk
ref sdk MyApp.csproj
```

The SDK is selected using the input directory's `global.json` context. An explicit SDK
installation directory is also accepted.

## Use it in scripts

Add `--json` for structured output or `--plain` for simple text. The normal terminal view
uses tables and trees. JSON is compact and uses schema version **2**; structured errors
have a distinct `code` and, when available, `input` and `candidates`.

| Exit code | Meaning |
| --- | --- |
| `0` | The requested inspection completed or the check passed |
| `1` | A rule failed, a dependency was not found, a publish failed, or a package review has incomplete evidence |
| `2` | RefTrace could not complete the request or reach a reliable conclusion |

A successful inspection means it read the input successfully. It is not a blanket claim
that the app is safe or AOT-compatible.

For report formats and exact limits, see the [technical reference](docs/REFERENCE.md).
For repeatable package, source and DLL comparisons, see [package audits](docs/PACKAGE-AUDIT.md).
For verified async API choices and a reusable modernization goal, see [async and threading](docs/ASYNC-THREADING.md).
The latest application of that goal is the [RefTrace self-review](docs/SELF-REVIEW.md).
The follow-up [transitive compatibility review](docs/TRANSITIVE-REVIEW.md) records
the five accepted dependency overrides and removal of unused test telemetry.
For the code layout and design decisions, see [architecture](ARCHITECTURE.md).
Changes are listed in [CHANGELOG.md](CHANGELOG.md).

RefTrace is [MIT licensed](LICENSE). Dependencies keep their own licenses;
see [third-party notices](THIRD-PARTY-NOTICES.md).
