"""Run existing tools with argument lists and preserve their exact outputs."""
from __future__ import annotations

import difflib
import json
from pathlib import Path
import subprocess

from .nuget import AuditError, Cache, assemblies, source_url, unpack, validate_hash


def run(arguments: list[str], *, timeout: int = 120) -> str:
    result = subprocess.run(arguments, text=True, capture_output=True, timeout=timeout)
    if result.returncode:
        raise AuditError(f"Command failed ({result.returncode}): {arguments[0]}\n{result.stderr[-2000:]}")
    return result.stdout


def ref_json(ref: list[str], arguments: list[str]) -> dict:
    value = json.loads(run([*ref, *arguments, "--json"]))
    if value.get("schemaVersion") != 1 or "data" not in value:
        raise AuditError("Unsupported RefTrace report schema")
    return value["data"]


def package_evidence(package: str, version: str, folder: Path, cache: Cache,
                     ref: list[str], ilspy: str, probes: dict, feed: dict) -> dict:
    key = package.lower()
    base = f'{feed["content"]}{key}/{version.lower()}/{key}.{version.lower()}'
    data = cache.get(base + ".nupkg", immutable=True)
    registration = json.loads(cache.get(f'{feed["registration"]}{key}/{version.lower()}.json'))
    catalog_url = registration["catalogEntry"]
    catalog = json.loads(cache.get(catalog_url, immutable=True))
    if catalog.get("packageHashAlgorithm") != "SHA512":
        raise AuditError("Unsupported package hash algorithm in the NuGet catalog")
    validate_hash(data, catalog["packageHash"].encode())
    archive = folder / "package"
    metadata = unpack(data, archive)
    if metadata["id"].lower() != key:
        raise AuditError("The package ID does not match its requested coordinate")
    metadata["integrity"] = "SHA-512 matches NuGet feed; not a signature or reproducible-build proof"
    metadata["hashSource"] = catalog_url
    metadata["listed"] = registration.get("listed")
    metadata["published"] = registration.get("published")
    metadata["assemblies"] = []
    for dll in assemblies(archive):
        target = folder / (dll.stem + ".assembly.json")
        evidence = ref_json(ref, ["assembly", str(dll), "--type", "*"])
        target.write_text(json.dumps(evidence, indent=2) + "\n")
        metadata["assemblies"].append({"path": str(dll.relative_to(archive)),
                                      "report": str(target), "targetFramework": evidence.get("targetFramework")})
        if dll.name.lower() == package.lower() + ".dll":
            for type_name in probes.get("types", []):
                text = run([ilspy, "-t", type_name, str(dll)])
                (folder / (type_name + ".cs")).write_text(text)
    metadata["source"] = []
    source_repository = metadata["repository"]
    fallback = probes.get("sourceFallback", {})
    if not source_repository and fallback.get("version") == version:
        source_repository = fallback["repository"]
        metadata["sourceProvenance"] = "configured upstream release; not declared by the nupkg"
    else:
        metadata["sourceProvenance"] = "nupkg repository declaration"
    for index, file in enumerate(probes.get("sourceFiles", [])):
        url = source_url(source_repository, file)
        if url is None:
            metadata["source"].append({"file": file, "status": "no-immutable-repository-coordinate"})
            continue
        try:
            text = cache.get(url, immutable=True)
            target = folder / f"source-{index}.cs"
            target.write_bytes(text)
            metadata["source"].append({"file": file, "url": url, "path": str(target),
                                       "status": "retrieved-at-nuspec-commit"})
        except AuditError as error:
            metadata["source"].append({"file": file, "url": url, "status": "unavailable", "error": str(error)})
    if not metadata["assemblies"]:
        metadata["notes"] = ["No supported ref/lib DLL; inspect extracted SDK/build/analyzer assets instead."]
    (folder / "evidence.json").write_text(json.dumps(metadata, indent=2) + "\n")
    return metadata


def compare(before: Path, after: Path, output: Path) -> list[str]:
    """Textual decompilation differences, not an API compatibility verdict."""
    reports = []
    for path in sorted(before.glob("*.cs")):
        other = after / path.name
        if path.name.startswith("source-") or not other.exists():
            continue
        diff = "".join(difflib.unified_diff(path.read_text().splitlines(True),
                    other.read_text().splitlines(True), fromfile=str(path), tofile=str(other)))
        target = output / (path.stem + ".diff")
        target.write_text(diff)
        reports.append(str(target))
    for path in sorted(before.glob("*.assembly.json")):
        other = after / path.name
        if not other.exists():
            continue
        def names(file: Path) -> set[str]:
            report = json.loads(file.read_text())
            return {entry["name"] for entry in report["types"]} | {
                f'{entry["name"]}::{member["kind"]}:{member["name"]}:{member["isStatic"]}'
                for entry in report["types"] for member in entry["members"]}
        old, new = names(path), names(other)
        target = output / (path.stem + ".names-diff.json")
        target.write_text(json.dumps({"added": sorted(new - old), "removed": sorted(old - new),
            "scope": "public names and member kinds; not overload signatures or binary compatibility"}, indent=2))
        reports.append(str(target))
    return reports
