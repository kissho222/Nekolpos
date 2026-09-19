#!/usr/bin/env python3
"""Codex hook guard for keeping .ai/HANDOFF.md current.

The guard deliberately does not edit HANDOFF.  The agent must summarize actual
results.  It records a small, per-turn timestamp in the operating system temp
directory and, at Stop, requests one continuation only when project files were
changed after the turn began and HANDOFF was not updated afterwards.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import tempfile
import time
from pathlib import Path
from typing import Any, Iterator


HANDOFF_RELATIVE_PATH = Path(".ai") / "HANDOFF.md"
STATE_DIRECTORY_NAME = "codex-handoff-guard"
SCAN_TIMEOUT_SECONDS = 8.0
TIME_TOLERANCE_SECONDS = 2.0
IGNORED_DIRECTORY_NAMES = {
    ".git",
    "Library",
    "Logs",
    "Temp",
    "obj",
    "Build",
    "Builds",
    "bin",
    "node_modules",
    "__pycache__",
}


def read_event() -> dict[str, Any]:
    try:
        event = json.load(sys.stdin)
    except json.JSONDecodeError:
        return {}
    return event if isinstance(event, dict) else {}


def find_workspace_root(cwd: str | None) -> Path | None:
    candidates = []
    if cwd:
        candidates.append(Path(cwd))
    # This fallback keeps the hook usable when Codex is launched from a child
    # directory but the script itself is stored in the workspace.
    candidates.append(Path(__file__).resolve().parents[2])

    for candidate in candidates:
        try:
            current = candidate.resolve()
        except OSError:
            continue
        for directory in (current, *current.parents):
            if (directory / HANDOFF_RELATIVE_PATH).is_file():
                return directory
    return None


def state_path(event: dict[str, Any]) -> Path:
    identifier = f"{event.get('session_id', '')}:{event.get('turn_id', '')}"
    digest = hashlib.sha256(identifier.encode("utf-8")).hexdigest()
    directory = Path(tempfile.gettempdir()) / STATE_DIRECTORY_NAME
    directory.mkdir(parents=True, exist_ok=True)
    return directory / f"{digest}.json"


def emit(payload: dict[str, Any]) -> int:
    print(json.dumps(payload, ensure_ascii=False))
    return 0


def write_prompt_state(event: dict[str, Any]) -> int:
    root = find_workspace_root(event.get("cwd"))
    if root is None:
        return emit({})

    handoff = root / HANDOFF_RELATIVE_PATH
    payload = {
        "root": str(root),
        "started_at": time.time(),
        "handoff_mtime_ns": handoff.stat().st_mtime_ns,
    }
    try:
        state_path(event).write_text(json.dumps(payload), encoding="utf-8")
    except OSError:
        # Failure to create the advisory state must never block a user turn.
        pass
    return emit({})


def iter_project_files(root: Path, deadline: float) -> Iterator[Path]:
    def walk(directory: Path) -> Iterator[Path]:
        if time.monotonic() > deadline:
            raise TimeoutError
        try:
            entries = list(os.scandir(directory))
        except OSError:
            return
        for entry in entries:
            if entry.name in IGNORED_DIRECTORY_NAMES:
                continue
            path = Path(entry.path)
            try:
                if entry.is_dir(follow_symlinks=False):
                    yield from walk(path)
                elif entry.is_file(follow_symlinks=False):
                    yield path
            except OSError:
                continue

    yield from walk(root)


def newest_change_after(root: Path, started_at: float) -> int | None:
    deadline = time.monotonic() + SCAN_TIMEOUT_SECONDS
    threshold_ns = int((started_at - TIME_TOLERANCE_SECONDS) * 1_000_000_000)
    handoff = (root / HANDOFF_RELATIVE_PATH).resolve()
    newest: int | None = None

    for path in iter_project_files(root, deadline):
        try:
            if path.resolve() == handoff:
                continue
            modified_ns = path.stat().st_mtime_ns
        except OSError:
            continue
        if modified_ns >= threshold_ns and (newest is None or modified_ns > newest):
            newest = modified_ns
    return newest


def check_stop(event: dict[str, Any]) -> int:
    # Codex sets this after honoring a Stop block.  Allowing it avoids an
    # automatic continuation loop if the agent still cannot update HANDOFF.
    if event.get("stop_hook_active") is True:
        return emit({})

    path = state_path(event)
    try:
        state = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return emit({})
    finally:
        try:
            path.unlink()
        except OSError:
            pass

    root = Path(state.get("root", ""))
    handoff = root / HANDOFF_RELATIVE_PATH
    if not root.is_dir() or not handoff.is_file():
        return emit({})

    try:
        newest_change = newest_change_after(root, float(state["started_at"]))
        handoff_mtime_ns = handoff.stat().st_mtime_ns
    except (KeyError, TypeError, ValueError, OSError, TimeoutError):
        # The guard is advisory.  Do not interrupt completion if a scan cannot
        # finish safely within its short timeout.
        return emit({})

    if newest_change is None or handoff_mtime_ns >= newest_change:
        return emit({})

    return emit(
        {
            "decision": "block",
            "reason": (
                "This turn changed project files after .ai/HANDOFF.md was last "
                "updated. Update HANDOFF with the actual changed files, checks, "
                "unresolved items, and confirmed decisions, then finish."
            ),
        }
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--event", choices=("prompt", "stop"), required=True)
    args = parser.parse_args()
    event = read_event()
    return write_prompt_state(event) if args.event == "prompt" else check_stop(event)


if __name__ == "__main__":
    raise SystemExit(main())
