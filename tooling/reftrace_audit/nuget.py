"""NuGet transport and immutable package evidence; no project mutations."""
from __future__ import annotations

import base64
import hashlib
import gzip
from http.client import IncompleteRead
import io
import json
import re
from pathlib import Path, PurePosixPath
import time
import tempfile
from threading import Lock
from urllib.error import HTTPError
from urllib.parse import urlparse
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET
import zipfile


class AuditError(RuntimeError):
    pass


MAX_DOWNLOAD = 256 * 1024 * 1024


class Cache:
    def __init__(self, root: Path, *, offline: bool = False, refresh: bool = False):
        self.root, self.offline, self.refresh = root, offline, refresh
        self.receipts: dict[str, dict] = {}
        self._locks: dict[str, Lock] = {}
        self._locks_guard = Lock()
        root.mkdir(parents=True, exist_ok=True)

    def get(self, url: str, *, immutable: bool = False) -> bytes:
        with self._locks_guard:
            lock = self._locks.setdefault(url, Lock())
        with lock:
            return self._get(url, immutable=immutable)

    def _get(self, url: str, *, immutable: bool) -> bytes:
        if urlparse(url).scheme != "https":
            raise AuditError(f"Expected HTTPS: {url}")
        path = self.root / hashlib.sha256(url.encode()).hexdigest()
        if path.exists() and (self.offline or (not self.refresh and
                (immutable or time.time() - path.stat().st_mtime < 3600))):
            receipt = json.loads(path.with_suffix(".json").read_text())
            data = path.read_bytes()
            if hashlib.sha256(data).hexdigest() != receipt["sha256"]:
                raise AuditError(f"Cached bytes do not match their receipt: {url}")
            self.receipts[url] = receipt
            return data
        if self.offline:
            raise AuditError(f"Not in offline cache: {url}")
        for attempt in range(3):
            try:
                request = Request(url, headers={"User-Agent": "RefTrace-package-audit/0.1"})
                with urlopen(request, timeout=30) as response:
                    # read(n) returns a body cut off by the network without an error when the
                    # response has a Content-Length; only a chunked body raises IncompleteRead.
                    data = response.read(MAX_DOWNLOAD + 1)
                    declared = response.headers.get("Content-Length")
                    chunked = "chunked" in response.headers.get("Transfer-Encoding", "").lower()
                if len(data) > MAX_DOWNLOAD:
                    raise AuditError(f"Download exceeds 256 MiB: {url}")
                if not chunked and declared is not None and len(data) < int(declared):
                    raise IncompleteRead(data, int(declared) - len(data))
                if not chunked and declared is None and immutable:
                    # Without either framing the body ends where the connection closes, so a cut-off
                    # body cannot be told from a complete one. Immutable entries are reused by every
                    # later run, so such a body is not accepted as evidence.
                    raise AuditError(f"Response declares neither Content-Length nor chunked encoding; "
                                     f"a complete body cannot be told from a cut-off one: {url}")
                if data[:2] == b"\x1f\x8b":
                    data = gzip.decompress(data)
                if len(data) > MAX_DOWNLOAD:
                    raise AuditError(f"Download exceeds 256 MiB: {url}")
                with tempfile.NamedTemporaryFile(dir=self.root, delete=False) as temporary:
                    temporary.write(data)
                    temporary_path = Path(temporary.name)
                temporary_path.replace(path)
                receipt = {
                    "url": url, "retrievedAtUnix": time.time(),
                    "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
                with tempfile.NamedTemporaryFile(dir=self.root, delete=False) as metadata:
                    metadata.write(json.dumps(receipt, indent=2).encode())
                    metadata_path = Path(metadata.name)
                metadata_path.replace(path.with_suffix(".json"))
                self.receipts[url] = receipt
                return data
            except HTTPError as error:
                if error.code not in (429, 502, 503, 504) or attempt == 2:
                    raise AuditError(f"HTTP {error.code}: {url}") from error
                time.sleep(attempt + 1)
            except IncompleteRead as error:
                if attempt == 2:
                    missing = "" if error.expected is None else f", {error.expected} more expected"
                    raise AuditError(f"Incomplete download ({len(error.partial)} bytes received{missing}); "
                                     f"nothing was cached: {url}") from error
                time.sleep(attempt + 1)
        raise AuditError(f"Could not retrieve {url}")


def validate_hash(data: bytes, expected: bytes) -> None:
    try:
        digest = base64.b64decode(expected.strip(), validate=True)
    except ValueError as error:
        raise AuditError("Invalid feed SHA-512 value") from error
    if hashlib.sha512(data).digest() != digest:
        raise AuditError("Package bytes do not match the feed's SHA-512 value")


def unpack(data: bytes, destination: Path) -> dict:
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        if sum(entry.file_size for entry in entries) > 1024 * 1024 * 1024:
            raise AuditError("Expanded package exceeds 1 GiB")
        for entry in entries:
            name = PurePosixPath(entry.filename)
            if name.is_absolute() or ".." in name.parts or "\\" in entry.filename or ":" in entry.filename:
                raise AuditError(f"Unsafe archive path: {entry.filename}")
            if ((entry.external_attr >> 16) & 0o170000) == 0o120000:
                raise AuditError(f"Archive contains a symlink: {entry.filename}")
        archive.extractall(destination)
    specs = list(destination.glob("*.nuspec"))
    if len(specs) != 1:
        raise AuditError("Expected one root nuspec")
    metadata = ET.parse(specs[0]).find("{*}metadata")
    if metadata is None:
        raise AuditError("Package has no metadata")
    if not metadata.findtext("{*}id") or not metadata.findtext("{*}version"):
        raise AuditError("Package metadata requires an ID and version")
    repository = metadata.find("{*}repository")
    return {"id": metadata.findtext("{*}id"), "version": metadata.findtext("{*}version"),
            "license": metadata.findtext("{*}license"),
            "repository": dict(repository.attrib) if repository is not None else {},
            "sha256": hashlib.sha256(data).hexdigest()}


def assemblies(directory: Path) -> list[Path]:
    """Prefer reference contracts, then one supported lib target per DLL name."""
    candidates = {folder.name for group in ("ref", "lib") for folder in (directory / group).glob("*")}
    def framework_key(name: str) -> tuple[int, int, int]:
        match = re.fullmatch(r"(net|netcoreapp|netstandard)(\d+)\.(\d+)", name)
        if match is None:
            return (-1, 0, 0)
        return ({"net": 2, "netcoreapp": 1, "netstandard": 0}[match[1]], int(match[2]), int(match[3]))
    frameworks = sorted((name for name in candidates if framework_key(name)[0] >= 0),
                        key=framework_key, reverse=True)
    selected: dict[str, Path] = {}
    for group in ("ref", "lib"):
        for framework in frameworks:
            for path in sorted((directory / group / framework).glob("*.dll")):
                selected.setdefault(path.name.lower(), path)
    return list(selected.values())


def source_url(repository: dict, file: str) -> str | None:
    remote = urlparse(repository.get("url", ""))
    commit = repository.get("commit", "")
    if remote.hostname != "github.com" or len(commit) != 40 or any(c not in "0123456789abcdefABCDEF" for c in commit):
        return None
    repo = remote.path.strip("/").removesuffix(".git")
    path = PurePosixPath(file)
    if len(repo.split("/")) != 2 or path.is_absolute() or ".." in path.parts:
        raise AuditError("Invalid source coordinate")
    return f"https://raw.githubusercontent.com/{repo}/{commit}/{path}"


def service_urls(cache: Cache) -> dict[str, str]:
    index = json.loads(cache.get("https://api.nuget.org/v3/index.json"))
    kinds = {item["@type"]: item["@id"] for item in index["resources"] if isinstance(item["@type"], str)}
    return {"content": kinds["PackageBaseAddress/3.0.0"],
            "registration": kinds["RegistrationsBaseUrl/3.6.0"]}
