import contextlib
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tooling.reftrace_audit.__main__ import inventory, main
from tooling.reftrace_audit.inspect import compare
from tooling.reftrace_audit.nuget import AuditError


class ReviewTests(unittest.TestCase):
    def test_inventory_merges_direct_status_and_keeps_nearest_sdk_pins(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "global.json").write_text(json.dumps({"msbuild-sdks": {"Example.Sdk": "2.0.0"}}))
            # Unused central declarations are not installed packages.
            (root / "Directory.Packages.props").write_text('<Project><ItemGroup><PackageVersion Include="Unused" Version="9.0"/></ItemGroup></Project>')
            projects = []
            for name in ("first", "second"):
                folder = root / name
                folder.mkdir()
                assets = folder / "assets.json"
                assets.write_text('{"targets":{"net11.0":{}}}')
                projects.append({"projectPath": str(folder / "App.csproj"), "assetsPath": str(assets)})
            (root / "second/global.json").write_text(json.dumps({"msbuild-sdks": {"Nested.Sdk": "1.0.0"}}))

            def packages(_ref, arguments):
                return {"packages": [{"id": "Shared", "version": "1.0.0", "direct": "second" in arguments[1]}]}

            with patch("tooling.reftrace_audit.__main__.ref_json", side_effect=packages):
                rows = inventory({"projects": projects}, ["ref"])
            by_name = {row["package"]: row for row in rows}
            self.assertEqual({"Shared", "Example.Sdk", "Nested.Sdk"}, set(by_name))
            self.assertTrue(by_name["Shared"]["direct"])
            self.assertEqual(2, len(by_name["Shared"]["origins"]))

    def review(self, rows, *, unavailable=(), unlisted=(), catalog=True, package=None, incomplete=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "inputs.json"
            manifest.write_text(json.dumps({"root": str(root), "projects": []}))
            probes = root / "probes.json"
            probes.write_text('{}')
            assembly = root / "RefTrace.Cli.dll"
            assembly.write_bytes(b"fixture")
            output = root / "output"
            output.mkdir()

            def get(url, **_kwargs):
                if not rows:
                    raise AssertionError("Network called for an empty inventory")
                if any(f"/{name}/" in url for name in unavailable):
                    raise AuditError("package unavailable")
                if url.endswith("/index.json"):
                    return b'{"versions":["1.0.0","2.0.0","3.0.0"]}'
                return json.dumps({"listed": not any(url.endswith(f"/{v}.json") for v in unlisted),
                                   "published": "2026-11-20T00:00:00+00:00"}).encode()

            def versions(_ref, arguments):
                available = json.loads(Path(arguments[1]).read_text())["versions"]
                selected = max(available)
                return {"current": "1.0.0", "selected": selected, "selection": "current" if selected == "1.0.0" else "stable"}

            args = ["--manifest", str(manifest), "--output", str(output), "--ref-assembly", str(assembly), "--probes", str(probes)]
            if catalog:
                args.append("--catalog-only")
            if package:
                args.extend(["--package", package])
            with (patch("tooling.reftrace_audit.__main__.inventory", return_value=copy.deepcopy(rows)),
                  patch("tooling.reftrace_audit.__main__.service_urls", return_value={"content": "https://feed/content/", "registration": "https://feed/registration/"}),
                  patch("tooling.reftrace_audit.__main__.Cache.get", side_effect=get),
                  patch("tooling.reftrace_audit.__main__.ref_json", side_effect=versions),
                  patch("tooling.reftrace_audit.__main__.run", return_value="fixture-version"),
                  patch("tooling.reftrace_audit.__main__.shutil.which", return_value=None),
                  patch("tooling.reftrace_audit.__main__.package_evidence", return_value={"source": [{"status": "unavailable" if incomplete else "retrieved-at-nuspec-commit"}]}),
                  contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO())):
                exit_code = main(args)
            return exit_code, json.loads((output / "report.json").read_text())

    @staticmethod
    def row(name):
        return {"package": name, "current": "1.0.0", "direct": True, "origins": ["fixture"]}

    def test_catalog_excludes_unlisted_candidate_and_applies_case_insensitive_filter(self):
        code, report = self.review([self.row("Example"), self.row("Other")], unlisted=["3.0.0"], package="ex*")
        self.assertEqual(0, code)
        self.assertEqual(2, report["schemaVersion"])
        self.assertEqual(1, len(report["packages"]))
        row = report["packages"][0]
        self.assertEqual("2.0.0", row["catalog"]["selected"])
        self.assertEqual(["3.0.0"], row["excludedUnlisted"])
        self.assertEqual("catalogued", row["status"])
        # The listed candidate's registration date feeds the minimum publish age check.
        self.assertEqual("2026-11-20T00:00:00+00:00", row["selectedPublished"])

    def test_failed_coordinate_does_not_hide_successful_package_report(self):
        code, report = self.review([self.row("Broken"), self.row("Working")], unavailable=["broken"])
        self.assertEqual(1, code)
        by_name = {row["package"]: row for row in report["packages"]}
        self.assertEqual("failed", by_name["Broken"]["status"])
        self.assertIn("unavailable", by_name["Broken"]["error"])
        self.assertEqual("catalogued", by_name["Working"]["status"])

    def test_missing_source_is_incomplete_not_success(self):
        code, report = self.review([self.row("Example")], catalog=False, incomplete=True)
        self.assertEqual(1, code)
        self.assertEqual("incomplete-source", report["packages"][0]["status"])
        self.assertEqual(2, len(report["packages"][0]["evidence"]))

    def test_empty_package_selection_needs_no_network_or_decompiler(self):
        code, report = self.review([], catalog=False)
        self.assertEqual(0, code)
        self.assertEqual([], report["packages"])
        self.assertIsNone(report["tools"]["ilspy"])

    def test_signature_diff_includes_added_and_removed_assemblies(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            before, after = root / "before", root / "after"
            before.mkdir()
            after.mkdir()
            report = {"types": [{"name": "Example", "kind": "class", "members": [{"signature": "public System.Int32 Value"}]}]}
            (before / "Old.assembly.json").write_text(json.dumps(report))
            (after / "New.assembly.json").write_text(json.dumps(report))
            paths = compare(before, after, root)
            self.assertEqual(2, len(paths))
            added = json.loads((root / "New.assembly.api-diff.json").read_text())
            removed = json.loads((root / "Old.assembly.api-diff.json").read_text())
            self.assertEqual([], added["removed"])
            self.assertEqual([], removed["added"])
            self.assertIn("Example::public System.Int32 Value", added["added"])
            self.assertIn("Example::public System.Int32 Value", removed["removed"])


if __name__ == "__main__":
    unittest.main()
