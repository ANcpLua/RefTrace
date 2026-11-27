# RefTrace

`ref` answers questions about .NET code from evidence: the packages a project restored, the metadata and IL of a
DLL, what a running process loaded, and whether a real Native AOT publish succeeds. It reads existing artifacts and
runs nothing from the code it inspects. When the evidence does not support a conclusion, it says so.

It is made for coding agents and scripts first: every command has `--json` output with a versioned schema, typed
error codes and a meaningful exit code. In a terminal, people get tables and trees.

## Install

```sh
dotnet tool install --global RefTrace
ref help
```

The package is **RefTrace**; the command is **ref**. It needs the .NET 11 runtime.
`dotnet tool update --global RefTrace` moves to a newer version. To pin the version per repository, install it into
a tool manifest and call `dotnet tool run ref`:

```sh
dotnet new tool-manifest
dotnet tool install RefTrace
```

`ref help` gives the overview. `ref help assembly` or `ref assembly --help` shows one command's arguments and options.
`ref version` or `ref --version` prints the installed version and the commit it was built from, as one line of text.

## Which command answers which question

| Question | Command | The answer rests on |
| --- | --- | --- |
| Which packages does this project resolve, and which of them directly? | `ref packages [input] [--direct] [--package 'glob'] [--assets]` | the restored `project.assets.json` |
| Why is this package in the graph? | `ref why <dependency> [input]` | one shortest path from the root project |
| What does the whole graph look like, project references included? | `ref assets [input]` | the same assets file |
| Does the graph contain something it must not contain? | `ref check [input] --forbid-package 'glob' --forbid-project 'glob'` | the graph; exit 1 when a forbidden dependency is found |
| Are newer package versions listed, and what changes with them? | `ref review [input] [--catalog-only] [--refresh] [--offline] [--package 'glob']` | NuGet release metadata, package bytes, declared API signatures |
| Which of a package's versions is the newest stable one, the newest prerelease, and the one to move to? | `ref versions <index.json> --current <version>` | a saved NuGet flat-container index, in NuGet's version order |
| What does this DLL contain, and what is the exact signature of this type? | `ref assembly <file> [--type 'Namespace.Type']` | metadata; the DLL is not loaded |
| Does this DLL call reflection or dynamic code? | `ref reflection <file> [--fail-on-findings]` | method references in IL |
| What points for or against Native AOT, and does a publish succeed? | `ref aot inspect <file>` and `ref aot publish <project> --rid <rid> --output <dir>` | declarations and annotations; a real publish |
| Which assemblies did this process load? | `ref processes`, then `ref runtime --pid <pid> [--seconds <n>] [--modules]` | the process's diagnostics endpoint, for that time window |
| Which SDK and compiler does this project select? | `ref sdk [input]` | the `global.json` context of the input directory |

`[input]` is a project, a `.slnx` solution with one .NET project, a directory with one project, or a restored assets
file; leave it out for the current directory. RefTrace asks MSBuild where the project keeps its assets. It does not
restore or build; when the restore is missing or belongs to another project, it returns `restore-required` or
`mismatched-assets` and you run `dotnet restore`. With several restored targets, add `--framework net11.0`. Quote
glob patterns so the shell passes them through.

## Read the result

Add `--json` for one line of JSON, or `--plain` for unstyled text. Every JSON report has the same envelope:

```json
{"schemaVersion": 2, "kind": "packages", "data": {}}
```

Check `schemaVersion` and tolerate additional fields. A failure has `kind` `error`; its `data` carries a stable `code`
such as `restore-required`, `ambiguous-project`, `ambiguous-target` or `file-not-found`, a message, and where they
apply the failing `input` and the valid `candidates` to choose from. The
[technical reference](https://github.com/ANcpLua/RefTrace/blob/main/docs/REFERENCE.md) lists every kind and code.

| Exit code | Meaning |
| --- | --- |
| `0` | The inspection completed, the dependency path exists, or the policy passed |
| `1` | A forbidden dependency was found, no path leads to the requested dependency, `reflection --fail-on-findings` found a call, a publish failed, or a package review has incomplete evidence |
| `2` | RefTrace could not complete the request or reach a reliable conclusion |

Treat exit code 2 as "no answer", never as a pass. For `runtime`, exit code 0 also requires a complete observation;
a partial one is returned with exit code 2.

What a result establishes:

- `packages`, `why`, `assets` and `check` describe the last restore of that project. A passed `check` is a statement
  about the graph. RefTrace is no vulnerability scanner.
- `assembly` and `reflection` read the file. A call that is present in a DLL may never run.
- `aot inspect` gives you things to review; its assessment stays `not-established`. `aot publish` runs the project's
  build with Native AOT enabled and warnings as errors, and its success holds for that configuration.
- `runtime` reports what the process loaded inside the observation window.

## Use it from a coding agent

Put the cases your repository cares about into its `AGENTS.md`, for example:

```markdown
## Dependencies and binaries: ask `ref`

- Read the real signature of an API before you rely on it:
  `ref assembly <path to the DLL> --type '<metadata name of the type>' --json`
- Before you add or raise a package: `ref review . --catalog-only --json`, then `ref why <package> --json`.
- After `dotnet restore`, prove a boundary: `ref check --forbid-package 'Unwanted.*' --json` exits with 0.
- A DLL that must stay free of reflection: `ref reflection <file> --fail-on-findings --json` exits with 0.
- Exit code 2 means RefTrace reached no conclusion. Report that and fix the input it names.
```

Type names are metadata names: `+` joins nested types, and a backtick with the number of type parameters marks a
generic type, as in ``System.Collections.Generic.List`1``.

The `debug-trace` skill in [`plugins/debug-trace`](plugins/debug-trace) brings this to an agent without an
`AGENTS.md` entry: it loads by itself when an agent debugs a failure or traces a request or dependency, in any
language, and reaches for `ref` where .NET is involved. This repository is also its plugin marketplace.

- Claude Code: `/plugin install debug-trace --marketplace ANcpLua/RefTrace`. As of 2026-11-27, Claude Code
  updates marketplaces outside Anthropic's only when asked: turn on auto-update for `reftrace` under `/plugin`,
  Marketplaces, so a push to this repository reaches your sessions.
- claude.ai and the Claude app: Customize, Plugins, Add marketplace `ANcpLua/RefTrace`, then turn on
  `debug-trace`. As of 2026-11-27, the Claude Code docs say cloud sessions load the plugins enabled there.
- Codex: link `plugins/debug-trace/skills/debug-trace` into `~/.agents/skills/`.

## Examples

Find a dependency:

```sh
ref packages MyApp.csproj --direct
ref packages MyApp.csproj --package 'NuGet.*' --assets
ref why Some.Package MyApp.csproj
ref check MyApp.csproj --forbid-package 'Unwanted.*' --forbid-project '*Other.Tests.csproj'
```

`--assets` adds compile, analyzer and runtime paths. Analyzer paths appear when the project sets
`RestoreEnableAnalyzerAssets` (NuGet 8.0, SDK 11.0.100).

Review package updates:

```sh
ref review . --catalog-only
ref review MyApp.slnx --catalog-only --refresh
ref review MyApp.csproj --package 'NuGet.*' --refresh
ref review MyApp.csproj --offline --json
```

`review` accepts one project, a `.slnx` solution or a repository with several projects. `--catalog-only` compares
the resolved versions with the listed NuGet releases. Without it, the review also collects package bytes, declared
API signatures and configured source probes. Every review saves its report in a new folder under
`artifacts/.package-audit/` next to the input and prints the path. It reports candidates and leaves your version
pins as they are. `review` needs uv and Python 3.14 on the machine, plus
`ilspycmd` for configured decompilation probes; see
[package reviews](https://github.com/ANcpLua/RefTrace/blob/main/docs/PACKAGE-AUDIT.md).

Look inside a DLL:

```sh
ref assembly MyLibrary.dll
ref assembly MyLibrary.dll --type 'MyNamespace.MyType'
ref reflection MyLibrary.dll
```

`assembly` shows versions, references and other recorded details. With `--type` it adds the declared public and
protected signatures: overloads, return types, generics, constructors, fields, events and property accessors.

Watch a running app:

```sh
ref processes
ref runtime --pid 12345 --seconds 1 --modules
```

Check Native AOT, and see what an installed SDK contains:

```sh
ref aot inspect MyLibrary.dll
ref aot publish MyApp.csproj --rid osx-arm64 --output artifacts/aot-check
ref sdk MyApp.csproj
```

`aot publish` writes its result and logs into a new folder below `--output` on every run. For a multi-targeted
project, add `--framework net11.0`.

## More

- [Technical reference](https://github.com/ANcpLua/RefTrace/blob/main/docs/REFERENCE.md): report formats, limits,
  the JSON contract, building from source.
- [Package reviews](https://github.com/ANcpLua/RefTrace/blob/main/docs/PACKAGE-AUDIT.md): repeatable package,
  source and DLL comparisons.
- [Async and threading](https://github.com/ANcpLua/RefTrace/blob/main/docs/ASYNC-THREADING.md): verified API choices
  and a reusable modernization goal. Its latest application is the
  [RefTrace self-review](https://github.com/ANcpLua/RefTrace/blob/main/docs/SELF-REVIEW.md), followed by the
  [transitive compatibility review](https://github.com/ANcpLua/RefTrace/blob/main/docs/TRANSITIVE-REVIEW.md).
- [Architecture](https://github.com/ANcpLua/RefTrace/blob/main/ARCHITECTURE.md): code layout and design decisions.
- [Changelog](https://github.com/ANcpLua/RefTrace/blob/main/CHANGELOG.md) and
  [publishing](https://github.com/ANcpLua/RefTrace/blob/main/docs/PUBLISHING.md).

RefTrace is [MIT licensed](https://github.com/ANcpLua/RefTrace/blob/main/LICENSE). Dependencies keep their own
licenses; see [third-party notices](https://github.com/ANcpLua/RefTrace/blob/main/THIRD-PARTY-NOTICES.md).
