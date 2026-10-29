import base64
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
import gzip
from unittest.mock import patch

from tooling.reftrace_audit.nuget import AuditError, Cache, assemblies, source_url, unpack, validate_hash
from tooling.reftrace_audit.inspect import compare, run
from tooling.reftrace_audit.__main__ import inventory


class EvidenceTests(unittest.TestCase):
    def test_inventory_preserves_each_restored_target_context(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "global.json").write_text("{}")
            path = root / "custom-restore/Example/assets.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({"targets": {"net10.0": {}, "net11.0/linux-x64": {}}}))
            with patch("tooling.reftrace_audit.__main__.ref_json", return_value={
                    "packages": [{"id": "Example", "version": "1.0.0", "direct": True}]}):
                rows = inventory({"projects": [{"projectPath": str(root / "Example.csproj"), "assetsPath": str(path)}]}, ["ref"])
            self.assertEqual(1, len(rows))
            self.assertEqual(2, len(rows[0]["origins"]))
            self.assertTrue(rows[0]["direct"])
            self.assertTrue(any(origin.endswith("#net11.0/linux-x64") for origin in rows[0]["origins"]))

    def test_hash_accepts_exact_bytes_and_rejects_changed_payload(self):
        digest = base64.b64encode(hashlib.sha512(b"package").digest())
        validate_hash(b"package", digest)
        with self.assertRaises(AuditError):
            validate_hash(b"changed", digest)

    def test_archive_rejects_traversal_and_symlink_before_extracting(self):
        for name, mode in [("../escape", None), ("/escape", None), ("link", 0o120777)]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                buffer = io.BytesIO()
                with zipfile.ZipFile(buffer, "w") as archive:
                    entry = zipfile.ZipInfo(name)
                    if mode is not None:
                        entry.external_attr = mode << 16
                    archive.writestr(entry, "data")
                with self.assertRaises(AuditError):
                    unpack(buffer.getvalue(), Path(directory))
                self.assertEqual([], list(Path(directory).iterdir()))

    def test_nuspec_identity_and_repository_are_preserved(self):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("Example.nuspec", '<package><metadata><id>Example</id><version>1.2.3</version>'
                '<license type="expression">MIT</license><repository type="git" url="https://github.com/a/b" '
                'commit="' + 'a' * 40 + '"/></metadata></package>')
        with tempfile.TemporaryDirectory() as directory:
            evidence = unpack(buffer.getvalue(), Path(directory))
            self.assertEqual("Example", evidence["id"])
            self.assertEqual("1.2.3", evidence["version"])
            self.assertEqual("MIT", evidence["license"])
            self.assertEqual("a" * 40, evidence["repository"]["commit"])

    def test_package_without_identity_is_rejected(self):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("Empty.nuspec", "<package><metadata/></package>")
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(AuditError, "ID and version"):
                unpack(buffer.getvalue(), Path(directory))

    def test_framework_selection_prefers_contract_then_newest_supported_lib(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in ["ref/net8.0/A.dll", "lib/net11.0/A.dll", "lib/net11.0/B.dll",
                             "lib/net8.0/B.dll", "runtimes/win-x64/native/C.dll"]:
                p = root / relative
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(b"")
            self.assertEqual({"ref/net8.0/A.dll", "lib/net11.0/B.dll"},
                             {p.relative_to(root).as_posix() for p in assemblies(root)})

    def test_source_requires_immutable_github_coordinate(self):
        repo = {"url": "https://github.com/a/b.git", "commit": "a" * 40}
        self.assertEqual("https://raw.githubusercontent.com/a/b/" + "a" * 40 + "/src/A.cs",
                         source_url(repo, "src/A.cs"))
        self.assertIsNone(source_url({**repo, "commit": "main"}, "src/A.cs"))
        with self.assertRaises(AuditError):
            source_url(repo, "../outside")

    def test_offline_cache_miss_is_not_silent_success(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Cache(Path(directory), offline=True)
            with self.assertRaises(AuditError):
                cache.get("https://api.nuget.org/missing")

    def test_gzip_response_and_offline_receipt_keep_the_same_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            url = "https://api.nuget.org/example.json"
            cache = Cache(Path(directory))
            with patch("tooling.reftrace_audit.nuget.urlopen", return_value=io.BytesIO(gzip.compress(b'{"value":1}'))):
                self.assertEqual(b'{"value":1}', cache.get(url))
            offline = Cache(Path(directory), offline=True)
            self.assertEqual(b'{"value":1}', offline.get(url))
            self.assertEqual(url, offline.receipts[url]["url"])

    def test_modified_cache_is_rejected_against_its_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            url = "https://api.nuget.org/example.json"
            cache = Cache(Path(directory))
            with patch("tooling.reftrace_audit.nuget.urlopen", return_value=io.BytesIO(b"original")):
                cache.get(url)
            (Path(directory) / hashlib.sha256(url.encode()).hexdigest()).write_bytes(b"changed")
            with self.assertRaisesRegex(AuditError, "Cached bytes"):
                Cache(Path(directory), offline=True).get(url)

    def test_decompiled_diff_preserves_changed_member_text(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            before, after = root / "before", root / "after"
            before.mkdir(); after.mkdir()
            (before / "A.cs").write_text("public int Old();\n")
            (after / "A.cs").write_text("public int New();\n")
            paths = compare(before, after, root)
            self.assertEqual(1, len(paths))
            text = Path(paths[0]).read_text()
            self.assertIn("-public int Old();", text)
            self.assertIn("+public int New();", text)

    def test_subprocess_failure_is_rejected(self):
        import sys
        with self.assertRaisesRegex(AuditError, "Command failed"):
            run([sys.executable, "-c", "raise SystemExit(9)"])

    def test_signature_diff_detects_changed_overload_with_same_member_name(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            before, after = root / "before", root / "after"
            before.mkdir(); after.mkdir()
            for folder, parameter in [(before, "System.Int32"), (after, "System.String")]:
                report = {"types": [{"name": "Example", "members": [
                    {"name": "Run", "signature": f"public System.Void Run({parameter} value)"}]}]}
                (folder / "Example.assembly.json").write_text(json.dumps(report))
            paths = compare(before, after, root)
            report = json.loads(Path(paths[0]).read_text())
            self.assertEqual(["Example::public System.Void Run(System.String value)"], report["added"])
            self.assertEqual(["Example::public System.Void Run(System.Int32 value)"], report["removed"])


if __name__ == "__main__":
    unittest.main()
