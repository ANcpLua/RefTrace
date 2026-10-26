# Package reviews

Run `make audit` to compare this checkout's package versions with NuGet and collect
the source and DLL evidence. `mise run audit` calls the same Make target. For only
the version inventory, use `make audit-catalog`.

The collector needs the SDK from `global.json`, uv, Python 3.14 and `ilspycmd` on PATH.
Its Python environment is owned by `uv.lock`; it has no third-party Python dependencies.
It does not change package references or publish anything.

Each run writes a new directory under `artifacts/package-audit/`. Its `report.json`
contains package origins, current and candidate versions, listing status, tool versions,
retrieval times, checksums and links to the local evidence. Failures are retained and
produce a nonzero exit code. The first audit needs network access. To repeat from saved
inputs without network access:

```sh
uv run --frozen python -m tooling.reftrace_audit --root . --offline
```

Use `--refresh` for new remote inputs. Mutable metadata is otherwise cached for one hour;
versioned package bytes and commit-addressed source files are reused. The report preserves
the original retrieval times so cached evidence is not presented as a new fetch.

## What is compared

| Evidence | Meaning |
| --- | --- |
| Project manifests and RefTrace's assets reports | Direct pins, SDK pins and resolved transitive package coordinates |
| NuGet service index, flat container and registration | Available versions and whether a candidate is listed |
| `ref versions` | NuGet's version ordering, including numeric prerelease identifiers |
| Catalog SHA-512 and downloaded archive | These package bytes match the feed's recorded digest |
| `ref assembly --type '*'` | Static assembly references and public type/member names |
| ILSpy output and diffs | Decompiled implementations of the configured types |
| Source files at the nuspec commit | Exact upstream files requested in `tooling/audit-probes.json` |

Reference assemblies are preferred, then the newest supported library target. The exact
asset path and target framework are recorded. Comparing two framework surfaces is not a
binary compatibility proof. Name diffs also do not compare overload signatures; use the
decompiled type and original source for that review.

The source probes are deliberately explicit. A missing repository coordinate or source
file is reported rather than replaced with a guessed branch. TraceEvent 4.0.0's package
omits its repository commit, so its configured release-tag revision is labelled as a
separate upstream source, not as a claim made by the package.

## Choosing an upgrade

A newer listed prerelease can be selected. An old preview does not replace a newer stable
version. Candidates for transitive dependencies are findings; update their owning direct
dependency before considering explicit overrides. After changing pins, run `make test
audit-test`, collect a new audit and inspect the packaged tool.

The C# `ReleaseDecision` union represents either keeping a version or selecting an upgrade.
Its switch expressions handle both cases and invalid default values explicitly. Python
handles network/filesystem effects; RefTrace owns the NuGet comparison and metadata reads.

This workflow and its maintenance boundaries are recorded in the local
[package-evidence skill](../.agents/skills/package-evidence/SKILL.md).
