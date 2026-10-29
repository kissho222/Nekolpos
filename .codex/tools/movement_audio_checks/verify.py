"""Compile complete current audio sources against memory-only Unity/Settings/Timeline doubles.

Python 3.10+ and .NET SDK 10. No Unity, real sound, settings, persistence or network.
Writes only a fresh directory under the selected repository's output directory.
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
import xml.sax.saxutils as xml

FILES = ("CharacterMovementAudioController.cs", "CatVoiceAudioController.cs", "FootstepSurfaceProfile.cs", "FootstepSurface.cs")
EXPECTED_COUNT = 67
BASELINE_FAILURES = frozenset((
    "regression-disabled-observe-rejected",
    "regression-disabled-jump-rejected",
    "regression-disabled-event-rejected",
    "regression-reenable-stale-ground-event-rejected",
    "regression-reenable-stale-airborne-no-landing",
    "reset-ground-event-needs-fresh-observation",
    "reset-airborne-no-phantom-landing",
    "reset-during-pause-retains-audio-pause-ownership",
    "suspend-stops-event-until-fresh-observation",
    "suspend-preserves-airborne-landing-across-pause",
))


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def validate_results(value: dict, exit_code: int) -> list[str]:
    if not isinstance(value, dict) or type(value.get("count")) is not int or value["count"] != EXPECTED_COUNT:
        raise ValueError("Unexpected case count")
    results = value.get("results")
    if not isinstance(results, list) or len(results) != EXPECTED_COUNT:
        raise ValueError("Missing case results")
    names = []
    failed = []
    for case in results:
        if not isinstance(case, dict) or not isinstance(case.get("name"), str) or not case["name"] or type(case.get("passed")) is not bool:
            raise ValueError("Invalid case result")
        names.append(case["name"])
        if not case["passed"]:
            failed.append(case["name"])
    if len(set(names)) != EXPECTED_COUNT or type(value.get("failures")) is not int or value["failures"] != len(failed):
        raise ValueError("Inconsistent case results")
    if exit_code != (1 if failed else 0):
        raise ValueError("Inconsistent process result")
    return failed


def main() -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=here.parents[2])
    parser.add_argument("--output", type=Path, help="A new directory under repo/output; existing results are never overwritten")
    parser.add_argument("--baseline", type=Path, help="Optional directory containing the same four complete pre-fix sources")
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    run = (args.output or repo / "output/movement-audio-checks" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8])).resolve()
    run.relative_to((repo / "output").resolve())
    run.mkdir(parents=True, exist_ok=False)
    manifest_path = run / "verification-manifest.json"
    manifest = {
        "schema": 1, "completed": False, "passed": False, "status": "incomplete",
        "startedUtc": datetime.now(timezone.utc).isoformat(), "variants": [],
        "unityLoaded": False, "realAudioPlayed": False, "realSettingsOrPersistenceCalled": False,
        "sourceCompilation": "Direct references to complete production files; no source extraction or transformation",
        "limitation": "Fake AudioSource call/state observations; no real mixer attenuation, audible quality, Unity lifecycle/physics/Timeline evaluation, scenes or Player execution.",
    }

    def save() -> None:
        temporary = run / "manifest.pending.json"
        temporary.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        temporary.replace(manifest_path)

    save()  # No environment/source check may leave an old success for this run.
    try:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            raise RuntimeError(".NET SDK 10 is required")
        environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
        sdk = subprocess.check_output([dotnet, "--version"], env=environment, text=True, timeout=30).strip()
        if not re.fullmatch(r"10\.[0-9]+\.[0-9]+(?:[-+].*)?", sdk):
            raise RuntimeError(".NET SDK 10 is required")
        manifest["sdk"] = sdk
        helpers = {name: (here / name).read_bytes() for name in ("Program.cs", "FakeUnity.cs")}
        manifest["toolHashes"] = {name: sha(data) for name, data in helpers.items()}
        manifest["toolHashes"]["verify.py"] = sha(Path(__file__).read_bytes())
        inputs = [("candidate", repo / "Assets/Scripts/Audio")]
        if args.baseline is not None:
            inputs.append(("baseline", args.baseline.resolve()))
        observed = []
        for variant, sources in inputs:
            directory = run / variant
            directory.mkdir()
            original = {name: (sources / name).read_bytes() for name in FILES}
            observed.append((sources, original))
            detail = {"variant": variant, "completed": False, "passed": False, "sources": [{"file": name, "sha256": sha(data), "bytes": len(data)} for name, data in original.items()], "missingTypes": []}
            manifest["variants"].append(detail)
            save()
            snapshot = directory / "source-snapshot"
            snapshot.mkdir()
            for name, data in original.items():
                (snapshot / name).write_bytes(data)
            # Snapshot is evidence only. Compile the actual input files and the shared doubles.
            project = directory / "AudioChecks.csproj"
            includes = [here / name for name in helpers] + [sources / name for name in FILES]
            include_xml = ''.join('<Compile Include="' + xml.escape(str(p.resolve()), {'"': '&quot;'}) + '" />' for p in includes)
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>' + include_xml + '</ItemGroup></Project>\n', encoding="utf-8")
            config = directory / "NuGet.Config"
            config.write_text('<configuration><packageSources><clear /></packageSources></configuration>\n', encoding="utf-8")
            for phase, command in (
                ("restore", [dotnet, "restore", str(project), "--configfile", str(config)]),
                ("compile", [dotnet, "build", str(project), "-c", "Release", "--no-restore", "--nologo"]),
                ("checks", [dotnet, str(directory / "bin/Release/net10.0/AudioChecks.dll")]),
            ):
                executed = subprocess.run(command, cwd=directory, env=environment, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
                (directory / (phase + ".stdout.txt")).write_text(executed.stdout, encoding="utf-8")
                (directory / (phase + ".stderr.txt")).write_text(executed.stderr, encoding="utf-8")
                detail[phase + "Exit"] = executed.returncode
                if phase == "compile":
                    detail["missingTypes"] = sorted(set(re.findall(r"error CS0246: The type or namespace name '([^']+)'", executed.stdout + executed.stderr)))
                if phase != "checks" and executed.returncode != 0:
                    raise RuntimeError("Build preparation failed")
                if phase == "checks":
                    checks = json.loads(executed.stdout)
                    failed = validate_results(checks, executed.returncode)
                    (directory / "results.json").write_text(json.dumps(checks, indent=2) + "\n", encoding="utf-8")
                    detail.update(count=checks["count"], failures=len(failed), failedNames=failed)
            detail["sourceUnchanged"] = all((sources / name).read_bytes() == data for name, data in original.items())
            expected = not detail["failedNames"] if variant == "candidate" else set(detail["failedNames"]) == BASELINE_FAILURES
            detail["semanticExpectationMet"] = expected
            detail["completed"] = True
            detail["passed"] = expected and detail["sourceUnchanged"]
            save()
        manifest["allSourcesUnchanged"] = all((sources / name).read_bytes() == data for sources, original in observed for name, data in original.items())
        manifest["toolSourcesUnchanged"] = all((here / name).read_bytes() == data for name, data in helpers.items()) and sha(Path(__file__).read_bytes()) == manifest["toolHashes"]["verify.py"]
        manifest["passed"] = all(v["passed"] for v in manifest["variants"]) and manifest["allSourcesUnchanged"] and manifest["toolSourcesUnchanged"]
        manifest["completed"] = True
        manifest["status"] = "passed" if manifest["passed"] else "failed"
        manifest["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        save()
        print(json.dumps({"manifest": str(manifest_path.relative_to(repo)), "completed": True, "passed": manifest["passed"], "variants": [{k: v[k] for k in ("variant", "count", "failures", "semanticExpectationMet")} for v in manifest["variants"]]}))
        return 0 if manifest["passed"] else 1
    except Exception as error:
        manifest["errorType"] = type(error).__name__
        save()
        print(json.dumps({"completed": False, "passed": False, "errorType": type(error).__name__}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(json.dumps({"completed": False, "passed": False, "errorType": type(error).__name__}), file=sys.stderr)
        raise SystemExit(2)
