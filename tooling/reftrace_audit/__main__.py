from __future__ import annotations

import argparse
import hashlib
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import sys
import subprocess
from urllib.error import URLError
import xml.etree.ElementTree as ET

from .inspect import compare, package_evidence, ref_json, run
from .nuget import AuditError, Cache, service_urls


def inventory(root: Path, ref: list[str]) -> list[dict]:
    records: dict[tuple[str, str], dict] = {}

    def add(name: str, version: str, origin: str, direct: bool = False) -> None:
        key = (name.lower(), version.lower())
        row = records.setdefault(key, {"package": name, "current": version,
                                      "direct": False, "origins": []})
        row["direct"] |= direct
        if origin not in row["origins"]:
            row["origins"].append(origin)

    for item in ET.parse(root / "Directory.Packages.props").iter():
        if item.tag.split("}")[-1] == "PackageVersion":
            add(item.attrib["Include"], item.attrib["Version"], "Directory.Packages.props", True)
    config = json.loads((root / "global.json").read_text())
    for name, version in config.get("msbuild-sdks", {}).items():
        add(name, version, "global.json:msbuild-sdks", True)
    for path in sorted((root / "artifacts/obj").glob("*/project.assets.json")):
        targets = json.loads(path.read_text()).get("targets")
        if not isinstance(targets, dict) or not targets:
            raise AuditError(f"No restored targets in {path}")
        for target in sorted(targets):
            result = ref_json(ref, ["packages", str(path), "--framework", target])
            for package in result["packages"]:
                add(package["id"], package["version"], f"{path.relative_to(root)}#{target}")
    return sorted(records.values(), key=lambda r: (r["package"].lower(), r["current"]))


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect versioned NuGet/source/DLL evidence without changing packages.")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path)
    parser.add_argument("--catalog-only", action="store_true")
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--refresh", action="store_true")
    parser.add_argument("--workers", type=int, choices=range(1, 5), default=2)
    parser.add_argument("--ilspy", default="ilspycmd")
    options = parser.parse_args()
    root = options.root.resolve()
    output = (options.output or root / "artifacts/package-audit" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")).resolve()
    output.mkdir(parents=True, exist_ok=False)
    ref = ["dotnet", str(root / "artifacts/bin/RefTrace.Cli/release/RefTrace.Cli.dll")]
    cache = Cache(root / "artifacts/package-audit-cache", offline=options.offline, refresh=options.refresh)
    probes = json.loads((root / "tooling/audit-probes.json").read_text())
    feed = service_urls(cache)
    if not options.catalog_only and shutil.which(options.ilspy) is None:
        raise AuditError("ilspycmd is required for DLL/source comparison")
    rows = inventory(root, ref)

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
    report = {"schemaVersion": 1, "collectedAt": datetime.now(timezone.utc).isoformat(),
        "root": str(root), "packages": records,
        "tools": {"python": sys.version.split()[0], "dotnetSdk": run(["dotnet", "--version"]).strip(),
            "ref": run([*ref, "--version"]).strip(),
            "refAssemblySha256": hashlib.sha256(Path(ref[1]).read_bytes()).hexdigest(),
            "ilspy": None if options.catalog_only else run([options.ilspy, "--version"]).strip()},
        "sources": sorted(cache.receipts.values(), key=lambda item: item["url"]),
        "limitations": ["NuGet integrity hashes are not publisher identity or reproducible-build proofs.",
            "Nuspec repository commits are package-provided provenance claims.",
            "RefTrace reports metadata names; decompiled type diffs include method bodies and are not an API-compatibility verdict.",
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
