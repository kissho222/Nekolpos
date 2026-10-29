#!/usr/bin/env python3
"""Offline behavior checks for the current Reporter, using only fake Unity/Unityroom."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.sax.saxutils as xml

RELATIVE_SOURCE = "Assets/Scripts/System/UnityRoomTeachingRankingReporter.cs"
KNOWN_BASE = "930b607317b1755aa3c9070855cec3301e6b9a7c"
VARIANTS = {
    "windows-release": "UNITY_STANDALONE_WIN",
    "windows-development": "UNITY_STANDALONE_WIN;DEVELOPMENT_BUILD",
    "webgl-editor": "UNITY_WEBGL;UNITY_EDITOR",
    "webgl-release": "UNITY_WEBGL",
    "webgl-development": "UNITY_WEBGL;DEVELOPMENT_BUILD",
}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--output", type=Path)
    baseline_group = parser.add_mutually_exclusive_group()
    baseline_group.add_argument("--baseline", type=Path, help="Optional unguarded Reporter source for the negative control")
    baseline_group.add_argument("--baseline-ref", default=KNOWN_BASE, help="Local Git ref containing the unguarded Reporter; no fetch occurs")
    args = parser.parse_args()
    root = args.repo_root.resolve()
    out = (args.output or (root / "output/unityroom-reporter-checks")).resolve()
    source = root / RELATIVE_SOURCE
    program = Path(__file__).with_name("Program.cs")
    out.mkdir(parents=True, exist_ok=True)
    manifest_path = out / "verification-manifest.json"
    summary = {
        "source": RELATIVE_SOURCE, "passed": False, "completed": False, "status": "incomplete",
        "startedUtc": datetime.now(timezone.utc).isoformat(), "results": [],
    }
    # Invalidate a previous success before SDK, source or baseline checks can fail.
    manifest_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    dotnet = shutil.which("dotnet")
    if dotnet is None or not source.is_file() or not program.is_file():
        raise RuntimeError("Requires .NET SDK 10.0, the repository Reporter source and Program.cs")
    original = source.read_bytes()
    baseline = args.baseline.read_bytes() if args.baseline else subprocess.check_output(
        ["git", "show", f"{args.baseline_ref}:{RELATIVE_SOURCE}"], cwd=root, stderr=subprocess.PIPE)
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
    baseline_file = out / "baseline.cs"
    baseline_file.write_bytes(baseline)
    sdk = subprocess.check_output([dotnet, "--version"], env=env, text=True).strip()
    summary.update({
        "source": RELATIVE_SOURCE, "sourceSha256": sha(original), "baselineSha256": sha(baseline),
        "baselineRef": None if args.baseline else args.baseline_ref,
        "programSha256": sha(program.read_bytes()), "verifierSha256": sha(Path(__file__).read_bytes()),
        "sdk": sdk, "unityLoaded": False, "normalSaveAdapterUsed": False,
        "realNetworkClientUsed": False, "results": [],
        "limitation": "Complete Reporter source with fake Unity, discovery store and unityroom. Release isolated=true is a policy double; real release always reports false. No engine, asset, package, real network or Player verification.",
    })
    failed = False

    def run(command, directory, prefix):
        result = subprocess.run(command, cwd=directory, env=env, capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=120)
        (directory / (prefix + ".stdout.txt")).write_text(result.stdout, encoding="utf-8")
        (directory / (prefix + ".stderr.txt")).write_text(result.stderr, encoding="utf-8")
        return result

    for version, checked_source in (("candidate", source), ("baseline", baseline_file)):
        for variant, defines in VARIANTS.items():
            directory = out / version / variant
            directory.mkdir(parents=True, exist_ok=True)
            config = directory / "NuGet.Config"
            config.write_text("<configuration><packageSources><clear /></packageSources></configuration>\n", encoding="utf-8")
            project = directory / "ReporterProbe.csproj"
            def include(path):
                return '<Compile Include="' + xml.escape(os.path.relpath(path, directory), {'"': '&quot;'}) + '" />'
            project.write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                '<OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                '<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><DefineConstants>' + defines + '</DefineConstants>'
                '</PropertyGroup><ItemGroup>' + include(checked_source) + include(program) + '</ItemGroup></Project>\n', encoding="utf-8")
            restored = run([dotnet, "restore", str(project), "--configfile", str(config)], directory, "restore")
            if restored.returncode:
                raise RuntimeError(f"Offline restore failed: {version}/{variant}; see output logs")
            built = run([dotnet, "build", str(project), "-c", "Release", "--no-restore", "--nologo"], directory, "compile")
            if built.returncode:
                raise RuntimeError(f"Probe compilation failed: {version}/{variant}; see output logs")
            executed = run([dotnet, str(directory / "bin/Release/net10.0/ReporterProbe.dll")], directory, "result")
            data = json.loads(executed.stdout)
            by_name = {case["name"]: case for case in data["results"]}
            if version == "candidate":
                expected = executed.returncode == 0 and data["count"] == 18 and data["failures"] == 0
            else:
                expected_failures = 5 if data["platformAllowed"] else 14
                expected = (executed.returncode == 1 and data["count"] == 18
                            and data["failures"] == expected_failures
                            and not by_name["isolated-manual"]["passed"]
                            and (data["platformAllowed"] or not by_name["normal-manual"]["passed"]))
            failed |= not expected
            summary["results"].append({"source": version, "variant": variant, "defines": defines,
                "compileExit": built.returncode, "executionExit": executed.returncode, "count": data["count"],
                "failures": data["failures"], "semanticExpectationMet": expected,
                "result": str((directory / "result.stdout.txt").relative_to(out))})
            print(f'{version} {variant}: {data["count"] - data["failures"]}/{data["count"]}; expected={expected}', flush=True)
    summary["sourceUnchanged"] = source.read_bytes() == original
    failed |= not summary["sourceUnchanged"]
    summary["passed"] = not failed
    summary["completed"] = True
    summary["status"] = "failed" if failed else "passed"
    summary["finishedUtc"] = datetime.now(timezone.utc).isoformat()
    manifest_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return 1 if failed else 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, OSError, subprocess.SubprocessError, ValueError) as error:
        print(f"Verification could not complete: {error}", file=sys.stderr)
        raise SystemExit(2)
