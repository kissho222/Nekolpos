"""Synthetic checks for audit input validation and stale-success prevention.

Uses only synthetic files under output/audit-tool-checks. Does not capture OS state,
read Registry/normal saves, or start Unity, dotnet, Git or a Player.
"""
from copy import deepcopy
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
TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parents[1]


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


audit = load("audit_under_test", TOOLS / "windows_state_audit.py")
verifier = load("reporter_verifier_under_test", TOOLS / "unityroom_reporter_checks/verify.py")
TREE = ("persistent", "Assets", "ProjectSettings", "Packages")
REGISTRY = ("nativePrefs32", "nativePrefs64", "editorPlayerPrefs32", "editorPlayerPrefs64", "editorPrefs32", "editorPrefs64")
ROOT_ID = audit.token(".")
CHILD_ID = "a" * 64
ENTRY_ID = "b" * 64
HASH = "c" * 64
results = []


def snapshot() -> dict:
    return {"schema": 1, "identity": "d" * 64, "exemptions": [], "groups": {
        **{name: {"directory/" + ROOT_ID: "present", "file/" + CHILD_ID: "3:" + HASH} for name in TREE},
        **{name: {"key/" + ROOT_ID: "present", "value/" + ENTRY_ID: "1:" + HASH} for name in REGISTRY},
    }}


def check(name, action):
    try:
        action()
        results.append({"name": name, "passed": True})
    except Exception as error:
        results.append({"name": name, "passed": False, "errorType": type(error).__name__})


def assert_preserved():
    assert audit.compare(snapshot(), snapshot())["preserved"]


def assert_changed(operation, expected_change):
    before, after = snapshot(), snapshot()
    operation(after)
    result = audit.compare(before, after)
    assert not result["preserved"] and result["changeCount"] == 1
    assert result["changes"][0]["change"] == expected_change


check("valid-identical", assert_preserved)
check("content-change", lambda: assert_changed(lambda s: s["groups"]["Assets"].update({"file/" + CHILD_ID: "4:" + HASH}), "changed"))
check("file-removed", lambda: assert_changed(lambda s: s["groups"]["Assets"].pop("file/" + CHILD_ID), "removed"))
check("file-added", lambda: assert_changed(lambda s: s["groups"]["Assets"].update({"file/" + ENTRY_ID: "0:" + HASH}), "added"))


def valid_absent():
    value = snapshot()
    value["groups"]["persistent"] = {"root": "absent"}
    assert audit.compare(value, deepcopy(value))["preserved"]


check("valid-absent-root", valid_absent)


def malformed(name, mutate):
    def rejected():
        value = snapshot()
        mutate(value)
        # The same malformed input on both sides must never become preserved=true.
        try:
            audit.compare(value, deepcopy(value))
        except ValueError:
            return
        raise AssertionError("Malformed snapshot accepted")
    check(name, rejected)


malformed("schema-missing", lambda s: s.pop("schema"))
malformed("schema-bool", lambda s: s.update(schema=True))
malformed("identity-missing", lambda s: s.pop("identity"))
malformed("identity-null", lambda s: s.update(identity=None))
malformed("identity-empty", lambda s: s.update(identity=""))
malformed("identity-short", lambda s: s.update(identity="d" * 63))
malformed("identity-nonhex", lambda s: s.update(identity="z" * 64))
malformed("exemptions-missing", lambda s: s.pop("exemptions"))
malformed("exemptions-nonempty", lambda s: s.update(exemptions=["ignored"]))
malformed("exemptions-wrongtype", lambda s: s.update(exemptions={}))
malformed("groups-missing", lambda s: s.pop("groups"))
malformed("groups-empty", lambda s: s.update(groups={}))
malformed("group-missing", lambda s: s["groups"].pop("nativePrefs64"))
malformed("group-unexpected", lambda s: s["groups"].update(extra={"root": "absent"}))
malformed("group-empty", lambda s: s["groups"].update(Assets={}))
malformed("group-wrongtype", lambda s: s["groups"].update(Assets=[]))
malformed("tree-root-missing", lambda s: s["groups"]["Assets"].pop("directory/" + ROOT_ID))
malformed("registry-root-missing", lambda s: s["groups"]["nativePrefs32"].pop("key/" + ROOT_ID))
malformed("mixed-absent-root", lambda s: s["groups"]["Assets"].update(root="absent"))
malformed("invalid-absent-root", lambda s: s["groups"].update(Assets={"root": "present"}))
malformed("entry-key-wrongtype", lambda s: s["groups"]["Assets"].update({3: "present"}))
malformed("entry-value-wrongtype", lambda s: s["groups"]["Assets"].update({"file/" + CHILD_ID: None}))
malformed("entry-id-short", lambda s: s["groups"]["Assets"].update({"file/abc": "0:" + HASH}))
malformed("file-negative-size", lambda s: s["groups"]["Assets"].update({"file/" + CHILD_ID: "-1:" + HASH}))
malformed("file-invalid-hash", lambda s: s["groups"]["Assets"].update({"file/" + CHILD_ID: "3:bad"}))
malformed("directory-invalid-value", lambda s: s["groups"]["Assets"].update({"directory/" + CHILD_ID: "absent"}))
malformed("registry-invalid-kind", lambda s: s["groups"]["nativePrefs32"].update({"value/" + ENTRY_ID: "99:" + HASH}))
malformed("registry-file-entry", lambda s: s["groups"]["nativePrefs32"].update({"file/" + CHILD_ID: "3:" + HASH}))
malformed("tree-registry-entry", lambda s: s["groups"]["Assets"].update({"value/" + ENTRY_ID: "1:" + HASH}))


def identity_mismatch():
    before, after = snapshot(), snapshot()
    after["identity"] = "e" * 64
    try:
        audit.compare(before, after)
    except ValueError:
        return
    raise AssertionError("Different identities accepted")


check("identity-mismatch", identity_mismatch)
synthetic_root = REPO / "output/audit-tool-checks"
synthetic_root.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix="synthetic-", dir=synthetic_root)).resolve()
run.relative_to(synthetic_root.resolve())


def compare_command(case):
    fixture = run / case
    output = fixture / "output"
    output.mkdir(parents=True)
    before, after = snapshot(), snapshot()
    expected = 0
    if case == "changed":
        after["groups"]["Assets"]["file/" + CHILD_ID] = "4:" + HASH
        expected = 1
    elif case == "invalid":
        before["groups"] = after["groups"] = {}
        expected = 2
    before_file, after_file = output / "before.json", output / "after.json"
    before_file.write_text(json.dumps(before), encoding="utf-8")
    after_file.write_text(json.dumps(after), encoding="utf-8")
    destination = output / "result.json"
    if case == "existing-output":
        destination.write_text("existing evidence", encoding="utf-8")
        expected = 2
    args = ["windows_state_audit.py", "--repo-root", str(fixture), "compare",
            "--before", str(before_file), "--after", str(after_file), "--out", str(destination)]
    with patch.object(sys, "argv", args), redirect_stderr(io.StringIO()), redirect_stdout(io.StringIO()):
        assert audit.main() == expected
    if case == "invalid":
        assert not destination.exists()
    elif case == "existing-output":
        assert destination.read_text(encoding="utf-8") == "existing evidence"
    else:
        assert json.loads(destination.read_text(encoding="utf-8"))["preserved"] is (expected == 0)


for case in ("unchanged", "changed", "invalid", "existing-output"):
    check("compare-command-" + case, lambda case=case: compare_command(case))


def stale_manifest(case):
    fixture = run / case
    source = fixture / verifier.RELATIVE_SOURCE
    source.parent.mkdir(parents=True)
    source.write_text("// synthetic source; never compiled\n", encoding="utf-8")
    baseline = fixture / "baseline.cs"
    if case != "missing-baseline":
        baseline.write_text("// synthetic baseline; never compiled\n", encoding="utf-8")
    output = fixture / "output"
    output.mkdir()
    manifest = output / "verification-manifest.json"
    manifest.write_text(json.dumps({"passed": True, "completed": True, "status": "passed"}), encoding="utf-8")
    args = ["verify.py", "--repo-root", str(fixture), "--output", str(output), "--baseline", str(baseline)]
    completed = [subprocess.CompletedProcess([], 0, "", ""), subprocess.CompletedProcess([], 1, "synthetic compile failure", "")]
    with patch.object(sys, "argv", args), patch.object(verifier.shutil, "which", return_value=None if case == "missing-sdk" else "mock-dotnet"), patch.object(verifier.subprocess, "check_output", return_value="10.0.mock\n"), patch.object(verifier.subprocess, "run", side_effect=completed) as launch:
        try:
            verifier.main()
        except (RuntimeError, FileNotFoundError):
            pass
        else:
            raise AssertionError("Expected an incomplete execution")
        assert launch.call_count == (2 if case == "compile-failure" else 0)
    state = json.loads(manifest.read_text(encoding="utf-8"))
    assert state["status"] == "incomplete" and state["completed"] is False and state["passed"] is False


for case in ("missing-sdk", "missing-baseline", "compile-failure"):
    check("stale-success-" + case, lambda case=case: stale_manifest(case))

report = {"scope": "Synthetic inputs/files and mocked subprocess only; no Registry, normal saves, Unity or dotnet execution", "passed": sum(r["passed"] for r in results), "total": len(results), "results": results}
(run / "results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, indent=2))
raise SystemExit(0 if report["passed"] == report["total"] else 1)
