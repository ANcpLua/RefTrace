from __future__ import annotations

import argparse
import fnmatch
import hashlib
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import shutil
import sys
import subprocess
from urllib.error import URLError

from .inspect import compare, package_evidence, ref_json, run
from .nuget import AuditError, Cache, service_urls


def inventory(manifest: dict, ref: list[str]) -> list[dict]:
    records: dict[tuple[str, str], dict] = {}

    def add(name: str, version: str, origin: str, direct: bool = False) -> None:
        if not re.fullmatch(r"[A-Za-z0-9_][A-Za-z0-9_.-]*", name) or not re.fullmatch(r"[0-9][A-Za-z0-9.+_-]*", version):
            raise AuditError(f"Invalid package coordinate: {name}/{version}")
        key = (name.lower(), version.lower())
        row = records.setdefault(key, {"package": name, "current": version,
                                      "direct": False, "origins": []})
        row["direct"] |= direct
        if origin not in row["origins"]:
            row["origins"].append(origin)

    sdk_configs: set[Path] = set()
    for project in manifest["projects"]:
        path = Path(project["assetsPath"])
        targets = json.loads(path.read_text())["targets"]
        if not isinstance(targets, dict) or not targets:
            raise AuditError(f"No restored targets in {path}")
        for target in sorted(targets):
            result = ref_json(ref, ["packages", str(path), "--framework", target])
            for package in result["packages"]:
                add(package["id"], package["version"], f"{path}#{target}", package["direct"])
        for directory in Path(project["projectPath"]).parents:
            config = directory / "global.json"
            if config.exists():
                sdk_configs.add(config)
                break
    for config in sorted(sdk_configs):
        for name, version in json.loads(config.read_text()).get("msbuild-sdks", {}).items():
            add(name, version, f"{config}:msbuild-sdks", True)
    return sorted(records.values(), key=lambda row: (row["package"].lower(), row["current"]))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Collect versioned NuGet/source/DLL evidence without changing packages.")
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--ref-assembly", type=Path, required=True)
    parser.add_argument("--probes", type=Path, required=True)
    parser.add_argument("--package")
    parser.add_argument("--catalog-only", action="store_true")
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--refresh", action="store_true")
    parser.add_argument("--workers", type=int, choices=range(1, 5), default=2)
    parser.add_argument("--ilspy", default="ilspycmd")
    options = parser.parse_args(argv)
    manifest = json.loads(options.manifest.read_text())
    root = Path(manifest["root"]).resolve()
    output = options.output.resolve()
    if (output / "report.json").exists():
        raise AuditError("A review report already exists in the requested output directory")
    ref = ["dotnet", str(options.ref_assembly.resolve())]
    cache = Cache(root / "artifacts/package-audit-cache", offline=options.offline, refresh=options.refresh)
    probes = json.loads(options.probes.read_text())
    rows = inventory(manifest, ref)
    if options.package:
        rows = [row for row in rows if fnmatch.fnmatchcase(row["package"].lower(), options.package.lower())]
    feed = service_urls(cache) if rows else {}

    def audit(row: dict) -> dict:
        folder = output / row["package"] / row["current"]
        folder.mkdir(parents=True)
        try:
            url = f'{feed["content"]}{row["package"].lower()}/index.json'
            index = folder / "index.json"
            index.write_bytes(cache.get(url))
            row["catalog"] = ref_json(ref, ["versions", str(index), "--current", row["current"]])
            filtered = json.loads(index.read_text())
            row["excludedUnlisted"] = []
            while row["catalog"]["selection"] != "current":
                selected = row["catalog"]["selected"]
                registration = json.loads(cache.get(f'{feed["registration"]}{row["package"].lower()}/{selected.lower()}.json'))
                if registration.get("listed") is True:
                    row["selectedPublished"] = registration.get("published")
                    break
                row["excludedUnlisted"].append(selected)
                filtered["versions"] = [v for v in filtered["versions"] if v.lower() != selected.lower()]
                candidate_index = folder / "listed-candidates.json"
                candidate_index.write_text(json.dumps(filtered))
                row["catalog"] = ref_json(ref, ["versions", str(candidate_index), "--current", row["current"]])
            row["indexUrl"] = url
            selected = row["catalog"]["selected"]
            coordinate = row["catalog"]["current"]
            row["status"] = "catalogued"
            if not options.catalog_only and (row["direct"] or row["catalog"]["selection"] != "current"):
                row["evidence"] = []
                for version in dict.fromkeys([coordinate, selected]):
                    target = folder / version
                    target.mkdir()
                    row["evidence"].append(package_evidence(row["package"], version, target, cache,
                        ref, options.ilspy, probes.get(row["package"], {}), feed))
                if selected != coordinate:
                    row["decompiledTypeDiffs"] = compare(folder / coordinate, folder / selected, folder)
                row["status"] = "collected"
                if any(source["status"] != "retrieved-at-nuspec-commit"
                       for evidence in row["evidence"] for source in evidence["source"]):
                    row["status"] = "incomplete-source"
        except (AuditError, OSError, ValueError, KeyError, URLError, subprocess.SubprocessError) as error:
            row["status"] = "failed"
            row["error"] = str(error)
        print(f'{row["package"]} {row["current"]}: {row["status"]}', file=sys.stderr, flush=True)
        return row

    with ThreadPoolExecutor(max_workers=options.workers) as pool:
        records = list(pool.map(audit, rows))
    report = {"schemaVersion": 2, "collectedAt": datetime.now(timezone.utc).isoformat(),
        "root": str(root), "projects": manifest["projects"], "packages": records,
        "tools": {"python": sys.version.split()[0], "collectorSdk": run(["dotnet", "--version"]).strip(),
            "ref": run([*ref, "--version"]).strip(),
            "refAssemblySha256": hashlib.sha256(Path(ref[1]).read_bytes()).hexdigest(),
            "ilspy": run([options.ilspy, "--version"]).strip() if not options.catalog_only and shutil.which(options.ilspy) else None},
        "sources": sorted(cache.receipts.values(), key=lambda item: item["url"]),
        "limitations": ["NuGet integrity hashes are not publisher identity or reproducible-build proofs.",
            "Nuspec repository commits are package-provided provenance claims.",
            "RefTrace reports declared metadata signatures; signature and implementation diffs are not an API-compatibility verdict.",
            "Only configured source files are retrieved. Missing source is explicitly recorded.",
            "Transitive candidates are reviewed, not injected as direct PackageReferences."]}
    report_file = output / "report.json"
    report_file.write_text(json.dumps(report, indent=2) + "\n")
    failed = [r for r in records if r["status"] in ("failed", "incomplete-source")]
    changed = [r for r in records if r.get("catalog", {}).get("selection") not in (None, "current")]
    print(f"{len(records)} package coordinates; {len(changed)} newer candidates; {len(failed)} failed collections")
    for row in records:
        if row["direct"]:
            print(f'{row["package"]}: {row["current"]} -> {row.get("catalog", {}).get("selected", "FAILED")}')
    print(report_file)
    return 1 if failed else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (AuditError, OSError, ValueError, KeyError, URLError) as error:
        print(f"audit: {error}", file=sys.stderr)
        sys.exit(2)
