# RefTrace

Find where your .NET dependencies come from and what your app loads.

RefTrace helps you follow a package back to its source, look inside a DLL, and check
a running app. It shows what it found and tells you when there is not enough information
to draw a conclusion.

Requires .NET 11 RC3.

## Install

The first NuGet release is pending. Install the current source with the .NET 11 RC3 SDK:

```sh
dotnet pack src/RefTrace.Cli -c Release -o artifacts/packages
dotnet tool install --global RefTrace --version 0.5.0-preview.1 --add-source artifacts/packages
ref help
```

After publication, install with `dotnet tool install --global RefTrace`.
The package is named **RefTrace**; the command is **ref**.
For an existing installation, replace `install` with `update`.

`ref help` gives a short overview. Use `ref help assembly` or `ref assembly --help`
for a command's arguments and options.

## Find a dependency

```sh
ref packages obj/project.assets.json
ref why obj/project.assets.json Some.Package
ref check obj/project.assets.json --forbid-package 'Unwanted.*'
```

`project.assets.json` is created when your project is restored. If it contains more than
one target, choose one with `--framework net11.0`.

Use `assets` for the full graph, including project references. To check a project boundary,
use `--forbid-project '*Other.Tests.csproj'`. Quote patterns so your shell passes them through.

## Look inside a DLL

```sh
ref assembly MyLibrary.dll
ref assembly MyLibrary.dll --type 'MyNamespace.MyType'
ref reflection MyLibrary.dll
```

`assembly` shows versions, references, and other recorded details. `reflection` shows
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
ref sdk /path/to/sdk-version
```

## Use it in scripts

Add `--json` for structured output or `--plain` for simple text. The normal terminal view
uses tables and trees.

| Exit code | Meaning |
| --- | --- |
| `0` | The requested inspection completed or the check passed |
| `1` | A rule failed, a dependency was not found, or a publish failed |
| `2` | RefTrace could not complete the request or reach a reliable conclusion |

A successful inspection means it read the input successfully. It is not a blanket claim
that the app is safe or AOT-compatible.

For report formats, exact limits, and release steps, see the [technical reference](docs/REFERENCE.md).
For the code layout and design decisions, see [architecture](ARCHITECTURE.md).
Changes are listed in [CHANGELOG.md](CHANGELOG.md).

RefTrace is [MIT licensed](LICENSE). Dependencies keep their own licenses;
see [third-party notices](THIRD-PARTY-NOTICES.md).
