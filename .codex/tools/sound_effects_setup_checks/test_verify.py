"""Synthetic failure checks for the verifier; no SDK/Unity execution."""
from __future__ import annotations

from contextlib import redirect_stderr
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from unittest.mock import patch


def main() -> None:
    here = Path(__file__).resolve().parent
    sys.dont_write_bytecode = True
    specification = importlib.util.spec_from_file_location("setup_verifier", here / "verify.py")
    verifier = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(verifier)
    output = here.parents[2] / "output/sound-effects-setup-checks-tool-tests"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="synthetic-", dir=output))
    cases = []
    for failure in ("final-manifest-write", "success-print"):
        repo = run / failure
        helper = repo / verifier.HELPER
        helper.parent.mkdir(parents=True)
        helper.write_text("// Synthetic source; never compiled or executed.\n", encoding="utf-8")
        destination = repo / "output/run"
        triggered = []
        original_write = Path.write_text
        original_print = print

        def write_file(path, data, *args, **kwargs):
            if failure == "final-manifest-write" and path.name == "manifest.pending.json" and not triggered:
                value = json.loads(data)
                if value.get("completed") is True and value.get("passed") is True:
                    triggered.append(failure)
                    raise OSError("Injected final manifest write failure")
            return original_write(path, data, *args, **kwargs)

        def print_result(*args, **kwargs):
            if failure == "success-print" and not triggered and args:
                value = json.loads(args[0])
                if value.get("completed") is True and value.get("passed") is True:
                    triggered.append(failure)
                    raise OSError("Injected success output failure")
            return original_print(*args, **kwargs)

        checks = json.dumps({"passed": 44, "total": 44, "cases": [f"synthetic-{index}" for index in range(44)]})
        with (patch.object(sys, "argv", ["verify.py", "--repo-root", str(repo), "--output", str(destination)]),
              patch.object(verifier.shutil, "which", return_value="not-executed-dotnet"),
              patch.object(verifier.subprocess, "check_output", return_value="10.0.401"),
              patch.object(verifier.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, checks, "")),
              patch.object(Path, "write_text", write_file), patch("builtins.print", print_result),
              redirect_stderr(io.StringIO())):
            exit_code = verifier.main()
        manifest = json.loads((destination / "verification-manifest.json").read_text(encoding="utf-8"))
        assert triggered == [failure], failure + ": failure was not injected"
        assert exit_code == 1, failure + ": unexpected exit code"
        assert manifest["completed"] is False and manifest["passed"] is False, failure + ": success leaked"
        assert manifest["status"] == "failed" and manifest["errorType"] == "OSError", failure + ": failure not recorded"
        cases.append({"name": failure, "passed": True})
    result = {"passed": 2, "total": 2, "cases": cases, "sdkExecuted": False, "unityLoaded": False,
              "scope": "Synthetic verifier failures only; not product case results"}
    (run / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"results": str(run / "results.json"), **result}))


if __name__ == "__main__":
    main()
