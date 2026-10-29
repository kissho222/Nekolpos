"""Synthetic verifier failure checks; no dotnet, Unity, Registry or normal save access."""
from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from unittest.mock import patch

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
spec = importlib.util.spec_from_file_location("audio_verifier", HERE / "verify.py")
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)
results = []


def valid():
    return {"count": verifier.EXPECTED_COUNT, "failures": 0, "results": [{"name": "case-" + str(i), "passed": True} for i in range(verifier.EXPECTED_COUNT)]}


def check(name, action):
    try:
        action()
        results.append({"name": name, "passed": True})
    except Exception as error:
        results.append({"name": name, "passed": False, "errorType": type(error).__name__})


def malformed(mutate, exit_code=0):
    value = valid()
    mutate(value)
    try:
        verifier.validate_results(value, exit_code)
    except ValueError:
        return
    raise AssertionError("Malformed results accepted")


check("valid-result", lambda: verifier.validate_results(valid(), 0))
check("missing-count", lambda: malformed(lambda v: v.pop("count")))
check("empty-results", lambda: malformed(lambda v: v.update(results=[])))
check("duplicate-case-name", lambda: malformed(lambda v: v["results"][1].update(name="case-0")))
check("nonboolean-pass", lambda: malformed(lambda v: v["results"][0].update(passed=1)))
check("failure-total-disagrees", lambda: malformed(lambda v: v.update(failures=1)))
check("exit-code-disagrees", lambda: malformed(lambda v: None, 1))

output_root = REPO / "output/movement-audio-checks-tool-tests"
output_root.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix="synthetic-", dir=output_root)).resolve()


def command_failure(case):
    fixture = run / case
    sources = fixture / "Assets/Scripts/Audio"
    sources.mkdir(parents=True)
    if case != "missing-source":
        for name in verifier.FILES:
            (sources / name).write_text("// synthetic input, never compiled\n", encoding="utf-8")
    destination = fixture / "output/result"
    args = ["verify.py", "--repo-root", str(fixture), "--output", str(destination)]
    if case == "missing-baseline":
        args.extend(["--baseline", str(fixture / "missing-baseline")])
    expected_exit = 2
    if case == "existing-output":
        destination.mkdir(parents=True)
        (destination / "verification-manifest.json").write_text("prior evidence", encoding="utf-8")

    def fake_run(command, **kwargs):
        if "restore" in command:
            return subprocess.CompletedProcess(command, 0, "", "")
        if "build" in command:
            return subprocess.CompletedProcess(command, 1 if case == "compile-failure" else 0, "synthetic compile result", "")
        if case == "source-changed":
            (sources / verifier.FILES[0]).write_text("// changed during execution\n", encoding="utf-8")
        return subprocess.CompletedProcess(command, 0, json.dumps(valid()), "")

    if case == "source-changed":
        expected_exit = 1
    with patch.object(sys, "argv", args), patch.object(verifier.shutil, "which", return_value=None if case == "missing-sdk" else "mock-dotnet"), patch.object(verifier.subprocess, "check_output", return_value="10.0.401\n"), patch.object(verifier.subprocess, "run", side_effect=fake_run) as process, redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
        if case == "existing-output":
            try:
                verifier.main()
            except FileExistsError:
                pass
            else:
                raise AssertionError("Existing output reused")
            assert process.call_count == 0
            assert (destination / "verification-manifest.json").read_text(encoding="utf-8") == "prior evidence"
            return
        assert verifier.main() == expected_exit
        assert process.call_count == {"missing-sdk": 0, "missing-source": 0, "compile-failure": 2, "missing-baseline": 3, "source-changed": 3}[case]
    state = json.loads((destination / "verification-manifest.json").read_text(encoding="utf-8"))
    assert state["passed"] is False
    if case == "source-changed":
        assert state["completed"] is True and state["status"] == "failed" and state["allSourcesUnchanged"] is False
    else:
        assert state["completed"] is False and state["status"] == "incomplete"


for case in ("missing-sdk", "missing-source", "compile-failure", "missing-baseline", "source-changed", "existing-output"):
    check(case, lambda case=case: command_failure(case))

report = {"scope": "Synthetic input/results and mocked subprocess only; no engine/audio/settings/persistence", "passed": sum(item["passed"] for item in results), "count": len(results), "results": results}
(run / "results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"report": str((run / "results.json").relative_to(REPO)), **report}, indent=2))
raise SystemExit(0 if report["passed"] == report["count"] else 1)
