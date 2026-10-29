"""Verify the complete Editor setup helper with managed Unity/Editor doubles.

Requires Python 3.10+ and .NET SDK 10. Writes only a new repo/output directory.
Does not load Unity, operate on actual scenes/assets, or access normal saves.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
from xml.sax.saxutils import escape

EXPECTED_COUNT = 44
HELPER = Path("Assets/Scripts/Editor/SoundEffectsSetupActions.cs")


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=here.parents[2])
    parser.add_argument("--output", type=Path, help="New directory under repo/output; existing results are never overwritten")
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    output_root = (repo / "output").resolve()
    run = (args.output or output_root / "sound-effects-setup-checks" / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8])).resolve()
    if run == output_root:
        raise ValueError("Use a new subdirectory of repo/output")
    run.relative_to(output_root)
    run.mkdir(parents=True, exist_ok=False)
    manifest_path = run / "verification-manifest.json"
    manifest = {
        "schema": 1, "completed": False, "passed": False, "status": "incomplete",
        "startedUtc": datetime.now(timezone.utc).isoformat(), "expectedCount": EXPECTED_COUNT,
        "unityLoaded": False, "actualAssetsScenesPrefsOrSavesUsed": False,
        "sourceCompilation": "Direct reference to complete production helper; no extraction or transformation",
        "limitation": "Managed Unity/Editor doubles only. No native Undo, serialization, Prefab overrides, import, scene authoring, lifecycle or playback verification.",
    }

    def save() -> None:
        temporary = run / "manifest.pending.json"
        temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        temporary.replace(manifest_path)

    save()
    stage = "prepare"
    try:
        source = (repo / HELPER).resolve()
        original = source.read_bytes()
        inputs = {name: (here / name).read_bytes() for name in ("Program.cs", "verify.py")}
        manifest["source"] = {"file": HELPER.as_posix(), "sha256": sha(original), "bytes": len(original)}
        manifest["toolHashes"] = {name: sha(data) for name, data in inputs.items()}
        # Snapshot is evidence only; the project compiles the actual production file.
        (run / "helper-source-snapshot.cs.txt").write_bytes(original)
        (run / "Program.cs").write_bytes(inputs["Program.cs"])
        project = run / "HelperChecks.csproj"
        helper_xml = escape(str(source), {'"': '&quot;'})
        project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>'
            '<ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable>'
            '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit>'
            '<ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>'
            '<ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>'
            '</PropertyGroup><ItemGroup><Compile Include="Program.cs" />'
            '<Compile Include="' + helper_xml + '" Link="SoundEffectsSetupActions.cs" />'
            '</ItemGroup></Project>\n', encoding="utf-8")
        config = run / "NuGet.Config"
        config.write_text('<configuration><packageSources><clear /></packageSources></configuration>\n', encoding="utf-8")
        temporary_directory = run / "temp"
        temporary_directory.mkdir()
        environment = dict(
            os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_HOME=str(run / ".dotnet"),
            NUGET_PACKAGES=str(run / ".nuget/packages"), NUGET_HTTP_CACHE_PATH=str(run / ".nuget/http-cache"),
            NUGET_PLUGINS_CACHE_PATH=str(run / ".nuget/plugins-cache"), NUGET_SCRATCH=str(run / ".nuget/scratch"),
            DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
            TMP=str(temporary_directory), TEMP=str(temporary_directory), TMPDIR=str(temporary_directory),
        )
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            raise RuntimeError(".NET SDK 10 is required")
        sdk = subprocess.check_output([dotnet, "--version"], cwd=run, env=environment, text=True, timeout=30).strip()
        if not re.fullmatch(r"10\.[0-9]+\.[0-9]+(?:[-+].*)?", sdk):
            raise RuntimeError(".NET SDK 10 is required")
        manifest["sdk"] = sdk
        save()
        for stage, command in (
            ("restore", [dotnet, "restore", str(project), "--configfile", str(config), "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false"]),
            ("compile", [dotnet, "build", str(project), "-c", "Release", "--no-restore", "--nologo", "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false", "-p:UseSharedCompilation=false"]),
            ("checks", [dotnet, str(run / "bin/Release/net10.0/HelperChecks.dll")]),
        ):
            result = subprocess.run(command, cwd=run, env=environment, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
            (run / (stage + ".stdout.txt")).write_text(result.stdout, encoding="utf-8")
            (run / (stage + ".stderr.txt")).write_text(result.stderr, encoding="utf-8")
            manifest[stage + "Exit"] = result.returncode
            if result.returncode != 0:
                raise RuntimeError("Verification phase failed")
        stage = "validate"
        checks = json.loads(result.stdout)
        names = checks.get("cases") if isinstance(checks, dict) else None
        if (not isinstance(names, list) or len(names) != EXPECTED_COUNT
                or not all(isinstance(name, str) and name for name in names)
                or len(set(names)) != EXPECTED_COUNT
                or type(checks.get("passed")) is not int or checks["passed"] != EXPECTED_COUNT
                or type(checks.get("total")) is not int or checks["total"] != EXPECTED_COUNT):
            raise ValueError("Unexpected or incomplete case results")
        (run / "results.json").write_text(json.dumps(checks, indent=2) + "\n", encoding="utf-8")
        manifest["count"] = EXPECTED_COUNT
        manifest["sourceUnchanged"] = source.read_bytes() == original
        manifest["toolSourcesUnchanged"] = all((here / name).read_bytes() == data for name, data in inputs.items())
        manifest["copiedFixtureUnchanged"] = (run / "Program.cs").read_bytes() == inputs["Program.cs"]
        manifest["passed"] = manifest["sourceUnchanged"] and manifest["toolSourcesUnchanged"] and manifest["copiedFixtureUnchanged"]
        manifest["completed"] = True
        manifest["status"] = "passed" if manifest["passed"] else "failed"
        manifest["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        save()
        print(json.dumps({"manifest": str(manifest_path), "completed": True, "passed": manifest["passed"], "count": EXPECTED_COUNT}))
        return 0 if manifest["passed"] else 1
    except Exception as error:
        manifest["completed"] = False
        manifest["passed"] = False
        manifest["failureStage"] = stage
        manifest["errorType"] = type(error).__name__
        manifest["status"] = "failed" if stage in ("checks", "validate") else "incomplete"
        manifest["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        save()
        print(json.dumps({"manifest": str(manifest_path), "completed": False, "passed": False, "failureStage": stage, "errorType": type(error).__name__}), file=sys.stderr)
        return 1 if stage in ("checks", "validate") else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(json.dumps({"completed": False, "passed": False, "errorType": type(error).__name__}), file=sys.stderr)
        raise SystemExit(2)
