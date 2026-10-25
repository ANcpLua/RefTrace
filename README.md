# RefTrace

Find where your .NET dependencies come from and what your app loads.

RefTrace helps you follow a package back to its source, look inside a DLL, and check
a running app. It shows what it found and tells you when there is not enough information
to draw a conclusion.

Requires .NET 10.

## Try it

From this directory, run any command through the project:

```sh
dotnet run --project src/RefTrace.Cli -- --help
```

The examples below use `reftrace`, the command installed by the tool package.
See [local installation](#build-and-install-locally) if you are working from this checkout.

## Find a dependency

```sh
reftrace packages obj/project.assets.json
reftrace why obj/project.assets.json Some.Package
reftrace check obj/project.assets.json --forbid-package 'Unwanted.*'
```

`project.assets.json` is created when your project is restored. If it contains more than
one target, choose one with `--framework net10.0`.

Use `assets` for the full graph, including project references. To check a project boundary,
use `--forbid-project '*Other.Tests.csproj'`. Quote patterns so your shell passes them through.

## Look inside a DLL

```sh
reftrace assembly MyLibrary.dll
reftrace assembly MyLibrary.dll --type 'MyNamespace.MyType'
reftrace reflection MyLibrary.dll
```

`assembly` shows versions, references, and other recorded details. `reflection` shows
known reflection and dynamic-code calls worth reviewing. A call found in a DLL does not
mean it ran. Neither command executes code from the DLL.

## Watch a running app

```sh
reftrace processes
reftrace runtime --pid 12345 --seconds 1 --modules
```

Choose a PID from the first command. The second shows assemblies observed in that process
and their file paths. It reports the time window and whether collection completed.
It cannot tell you what the app will load later.

## Check Native AOT

```sh
reftrace aot inspect MyLibrary.dll
reftrace aot publish MyApp.csproj --rid osx-arm64 --output artifacts/aot-check
```

`inspect` gives you things to review. `publish` tries a real Native AOT build and saves
its result and logs in a new folder. It runs the project's build steps; it does not upload
anything to NuGet. For a multi-targeted project, add `--framework net10.0`.

To see which compiler and native CLI files an installed SDK contains:

```sh
reftrace sdk /path/to/sdk-version
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

## Build and install locally

```sh
dotnet build RefTrace.slnx -c Release
dotnet test --solution RefTrace.slnx -c Release
dotnet pack src/RefTrace.Cli -c Release -o artifacts/packages
dotnet tool install RefTrace --version 0.3.0 --add-source artifacts/packages --tool-path artifacts/tools
./artifacts/tools/reftrace --help
```

If it is already installed in that folder, use `dotnet tool update` instead of `install`.

For report formats, exact limits, and release steps, see the [technical reference](docs/REFERENCE.md).
For the code layout and design decisions, see [architecture](ARCHITECTURE.md).
Changes are listed in [CHANGELOG.md](CHANGELOG.md).

RefTrace is [MIT licensed](LICENSE). Dependencies keep their own licenses;
see [third-party notices](THIRD-PARTY-NOTICES.md).
