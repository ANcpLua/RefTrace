# Package reviews

Use the installed `ref` command from the project or repository you want to inspect:

```sh
ref review . --catalog-only --refresh
ref review MyApp.csproj --package 'NuGet.*' --refresh
ref review MyApp.csproj --offline --json
```

A project input reviews its restored package graph. A directory reviews all discovered
projects, across every recorded target. RefTrace evaluates each project's assets path
through MSBuild and verifies the recorded project identity. Restore missing or stale
inputs yourself; review does not run restore/build targets or change package pins.
A failed project evaluation stops before package collection and returns a structured error.

The Python collector and default probes ship in the tool package. It requires **uv and
Python 3.14** already installed; review does not download a Python interpreter or install
Python packages. Only configured decompilation probes need **ilspycmd** on PATH. There
are no third-party Python dependencies. The source checkout uses `uv.lock` for its test
environment. `make audit` and `make audit-catalog` build and run the same command locally.

Each run writes a new directory under the input's `artifacts/package-audit/`, or the new
folder supplied with `--output`. `inputs.json` records project paths, evaluated assets
paths and SDK versions. `report.json` (saved-report schema 2) records origins, current and
candidate versions, listing status, tools, timestamps, checksums and evidence paths.
The command's JSON summary uses the CLI schema-2 envelope and links to this report.
Per-package failures remain in the report and cause exit 1. Missing tools, invalid input,
or failure to produce a report cause exit 2. Existing output directories are rejected.

`--catalog-only` checks versions without downloading package/source/DLL evidence.
`--package` is a case-insensitive ID glob. `--offline` uses only cached inputs and fails
on a cache miss. It still evaluates local projects and invokes local tools. `--refresh`
fetches remote inputs again; it cannot be combined with `--offline`. Otherwise mutable
metadata is cached for one hour and versioned package/source bytes are reused. Original
retrieval times are retained so a cache hit is not presented as a new fetch.

## What is compared

| Evidence | Meaning |
| --- | --- |
| Each restored assets target | Actual direct/transitive package coordinates, including custom output layouts |
| The nearest global.json for each project | Explicit MSBuild SDK package pins |
| NuGet service index, flat container and registration | Available versions and whether a candidate is listed |
| `ref versions` | NuGet's version ordering, including numeric prerelease identifiers |
| Catalog SHA-512 and downloaded archive | Package bytes match the feed's recorded digest |
| `ref assembly --type '*'` | Static references and declared public/protected API signatures |
| Signature diffs | Added/removed types, members and assemblies, including same-name overload changes |
| Optional ILSpy output and diffs | Decompiled implementations of configured types |
| Configured source files at the nuspec commit | Exact upstream files requested in the probe configuration |

Full reviews collect package evidence for direct dependencies and changed transitive
candidates. Unchanged transitives retain catalog evidence. Reference assemblies are
preferred, then the newest supported library target. Each selected asset and framework
is recorded. A signature difference is evidence to review, not a binary/source compatibility
verdict. Inherited members, nullable/custom-attribute semantics and all target frameworks
are not expanded by the signature comparison.

Default probes come from `tooling/audit-probes.json` in this source tree and are bundled
with the installed command. Supply your own file with `--probes /path/probes.json`:

```json
{
  "Example.Package": {
    "types": ["Example.PublicType"],
    "sourceFiles": ["src/Example/PublicType.cs"]
  }
}
```

Packages without probes still receive package and metadata evidence; that does not mean
source or implementations were reviewed. Missing configured source or decompilation tools
remain failures. Source retrieval uses immutable repository coordinates. The bundled
TraceEvent fallback is labelled as a separately configured upstream revision because its
package omits that provenance. Do not replace missing source with a guessed branch.

## Choosing an upgrade

A newer listed prerelease can be selected. An old preview does not replace a newer stable
version. Transitive candidates are findings; update their owning direct dependency before
considering explicit overrides. After changing pins, run the repository's build/tests,
collect a new review and inspect the packaged application.

RefTrace owns NuGet comparison and metadata decoding; Python handles network/filesystem
collection. The command produces evidence. The reusable modernization task in
[async and threading](ASYNC-THREADING.md) tells an agent how to verify and apply changes.
The source workflow is also recorded in the local
[package-evidence skill](../.agents/skills/package-evidence/SKILL.md).
