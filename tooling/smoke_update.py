"""Exercise the packaged updater against an isolated NuGet feed and tool installations."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time


ROOT = Path(__file__).resolve().parents[1]
OLD = "1.0.0"
NEW = "1.0.1"


def smoke_update(work: Path) -> None:
    feed = work / "package feed"
    pack = ["dotnet", "pack", ROOT / "src/RefTrace.Cli/RefTrace.Cli.csproj",
            "-c", "Release", "--artifacts-path", work / "build", "-o", feed]
    subprocess.run([*pack, f"-p:Version={OLD}"], cwd=ROOT, check=True)
    subprocess.run([*pack, f"-p:Version={NEW}", "--no-restore", "-p:BuildProjectReferences=false"],
                   cwd=ROOT, check=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()

    home = work / "home"
    home.mkdir()
    scratch = work / "tmp"
    scratch.mkdir()
    env = {
        **os.environ,
        "DOTNET_CLI_HOME": str(home),
        "DOTNET_NOLOGO": "1",
        "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
        "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
        "NUGET_PACKAGES": str(work / "cache"),
        "NUGET_HTTP_CACHE_PATH": str(work / "http-cache"),
        "TMPDIR": str(scratch),
        "TEMP": str(scratch),
        "TMP": str(scratch),
    }
    shutil.copyfile(ROOT / "global.json", work / "global.json")

    def run(*args, check=True) -> subprocess.CompletedProcess:
        result = subprocess.run(args, cwd=work, env=env, stdout=subprocess.PIPE,
                                text=True, timeout=120)
        print(result.stdout, end="", flush=True)
        if check:
            result.check_returncode()
        return result

    def check_version(command, version) -> None:
        actual = run(*command, "--version").stdout.strip()
        expected = f"{version}+{commit}"
        if actual != expected:
            raise AssertionError(f"{command} reported {actual!r}, expected {expected!r}")

    def update(command, spelling, source=feed, expect_success=True) -> None:
        process = run(*command, spelling, "--source", source, "--json", check=False)
        report = json.loads(process.stdout)
        if report["kind"] != "update":
            raise AssertionError(f"Unexpected update report: {report}")
        result = report["data"]
        if result["outcome"] == "scheduled":
            process.check_returncode()
            receipt = Path(result["resultFile"])
            deadline = time.monotonic() + 120
            while not receipt.exists():
                if time.monotonic() >= deadline:
                    raise TimeoutError(f"Update did not finish at {receipt}")
                time.sleep(0.25)
            result = json.loads(receipt.read_text(encoding="utf-8"))
        elif process.returncode != result["exitCode"]:
            raise AssertionError(f"Update returned {process.returncode} but reported {result}")
        expected = "succeeded" if expect_success else "failed"
        if result["outcome"] != expected or (result["exitCode"] == 0) != expect_success:
            raise AssertionError(f"Expected {expected} update, got {result}")

    tools = work / "installed tools"
    shim = "ref.exe" if os.name == "nt" else "ref"
    custom_tool = [tools / shim]
    global_tool = [home / ".dotnet" / "tools" / shim]
    manifest_tool = ["dotnet", "tool", "run", "ref", "--"]
    install = ["dotnet", "tool", "install", "RefTrace", "--version", OLD, "--source", feed]
    run(*install, "--tool-path", tools)
    run(*install, "--global")
    run("dotnet", "new", "tool-manifest")
    run(*install, "--local")

    for command in (custom_tool, global_tool, manifest_tool):
        check_version(command, OLD)
    update(custom_tool, "update", work / "missing feed", expect_success=False)
    check_version(custom_tool, OLD)
    update(custom_tool, "update")
    check_version(custom_tool, NEW)
    check_version(global_tool, OLD)
    check_version(manifest_tool, OLD)

    update(manifest_tool, "--update")
    check_version(manifest_tool, NEW)
    check_version(global_tool, OLD)
    update(global_tool, "update")
    check_version(global_tool, NEW)
    update(custom_tool, "--update")
    check_version(custom_tool, NEW)


if __name__ == "__main__":
    with tempfile.TemporaryDirectory(prefix="reftrace-update-") as directory:
        smoke_update(Path(directory))
