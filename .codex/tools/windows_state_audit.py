"""Read-only Windows preservation snapshots. Writes only a new file under repo/output.

No Unity, process launch, persistence adapter, registry mutation, restore, or exemptions.
Registry names/values and persistent file names are represented only by SHA-256.
Python 3.10+ standard library; snapshot requires Windows.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
from datetime import datetime, timezone

TREE_GROUPS = frozenset(("persistent", "Assets", "ProjectSettings", "Packages"))
REGISTRY_GROUPS = frozenset(
    name + view
    for name in ("nativePrefs", "editorPlayerPrefs", "editorPrefs")
    for view in ("32", "64")
)
HEX_DIGEST = re.compile(r"[0-9a-f]{64}\Z")
SIZED_DIGEST = re.compile(r"(?:0|[1-9][0-9]*):[0-9a-f]{64}\Z")


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def token(text: str) -> str:
    return digest(text.encode("utf-8", errors="surrogatepass"))


def output_path(repo: Path, value: str) -> Path:
    path = Path(value).resolve()
    path.relative_to((repo / "output").resolve())
    if path.exists():
        raise ValueError("Output already exists")
    return path


def write_new(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
        stream.write("\n")


def snapshot_tree(root: Path) -> dict:
    result: dict[str, str] = {}

    def visit(path: Path, relative: str) -> None:
        info = path.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError("Reparse point requires separate review")
        ident = token(relative)
        if stat.S_ISDIR(info.st_mode):
            result["directory/" + ident] = "present"
            children = sorted(path.iterdir(), key=lambda child: child.name)
            for child in children:
                visit(child, relative + "/" + child.name)
            if [child.name for child in children] != sorted(child.name for child in path.iterdir()):
                raise ValueError("Directory changed during capture")
        elif stat.S_ISREG(info.st_mode):
            hashed = hashlib.sha256()
            with path.open("rb") as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    hashed.update(block)
            after = path.stat()
            if (info.st_size, info.st_mtime_ns, info.st_ino) != (after.st_size, after.st_mtime_ns, after.st_ino):
                raise ValueError("File changed during capture")
            result["file/" + ident] = str(info.st_size) + ":" + hashed.hexdigest()
        else:
            raise ValueError("Unsupported filesystem entry")

    if not root.exists():
        return {"root": "absent"}
    visit(root, ".")
    return result


def snapshot_registry(subkey: str, view: int) -> dict:
    import winreg
    result: dict[str, str] = {}

    def visit(current: str, relative: str) -> None:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, current, 0, winreg.KEY_READ | view) as key:
            child_count, value_count, _ = winreg.QueryInfoKey(key)
            result["key/" + token(relative)] = "present"
            for index in range(value_count):
                name, value, kind = winreg.EnumValue(key, index)
                payload = value if isinstance(value, bytes) else json.dumps(
                    value, ensure_ascii=False, separators=(",", ":")
                ).encode("utf-8", errors="surrogatepass")
                result["value/" + token(relative + "\0" + name)] = str(kind) + ":" + digest(payload)
            children = [winreg.EnumKey(key, index) for index in range(child_count)]
        for child in children:
            visit(current + "\\" + child, relative + "/" + child)

    try:
        visit(subkey, ".")
    except FileNotFoundError:
        # Only a missing root is an allowed empty state; deletion during traversal fails.
        if result:
            raise
        return {"root": "absent"}
    return result


def product_settings(repo: Path) -> tuple[str, str]:
    source = (repo / "ProjectSettings/ProjectSettings.asset").read_text(encoding="utf-8-sig")
    values = []
    for field in ("companyName", "productName"):
        matches = re.findall(r"^  " + field + r": ([^\r\n]+)$", source, re.MULTILINE)
        if len(matches) != 1 or any(c in matches[0] for c in '\\/:*?"<>|') or matches[0] in (".", ".."):
            raise ValueError("Unsupported company/product setting")
        values.append(matches[0])
    return values[0], values[1]


def capture(repo: Path) -> dict:
    if sys.platform != "win32":
        raise RuntimeError("Windows required")
    import winreg
    company, product = product_settings(repo)
    persistent = Path(os.environ["USERPROFILE"]) / "AppData/LocalLow" / company / product
    registry_roots = {
        "nativePrefs": "Software\\" + company + "\\" + product,
        "editorPlayerPrefs": "Software\\Unity\\UnityEditor\\" + company + "\\" + product,
        "editorPrefs": "Software\\Unity Technologies\\Unity Editor 5.x",
    }
    roots = {"persistent": persistent}
    roots.update({part: repo / part for part in ("Assets", "ProjectSettings", "Packages")})
    groups = {name: snapshot_tree(path) for name, path in roots.items()}
    for name, path in registry_roots.items():
        for label, flag in (("64", winreg.KEY_WOW64_64KEY), ("32", winreg.KEY_WOW64_32KEY)):
            first = snapshot_registry(path, flag)
            if first != snapshot_registry(path, flag):
                raise ValueError("Registry changed during capture")
            groups[name + label] = first
    return {
        "schema": 1,
        "capturedUtc": datetime.now(timezone.utc).isoformat(),
        "identity": token(str(repo.resolve()) + "\0" + company + "\0" + product + "\0" + str(persistent)),
        "groups": groups,
        "exemptions": [],
        "notes": "Content/type/presence comparison; no registry/file values or original names. No restoration.",
    }


def validate_snapshot(snapshot: dict) -> None:
    if not isinstance(snapshot, dict) or type(snapshot.get("schema")) is not int or snapshot["schema"] != 1:
        raise ValueError("Invalid snapshot schema")
    identity = snapshot.get("identity")
    if not isinstance(identity, str) or HEX_DIGEST.fullmatch(identity) is None:
        raise ValueError("Invalid snapshot identity")
    if snapshot.get("exemptions") != []:
        raise ValueError("Snapshot exemptions must be an empty list")
    groups = snapshot.get("groups")
    if not isinstance(groups, dict) or set(groups) != TREE_GROUPS | REGISTRY_GROUPS:
        raise ValueError("Snapshot must contain every expected group")
    for group, entries in groups.items():
        if not isinstance(entries, dict) or not entries:
            raise ValueError("Invalid snapshot group")
        if entries == {"root": "absent"}:
            continue
        root_kind = "directory" if group in TREE_GROUPS else "key"
        if entries.get(root_kind + "/" + token(".")) != "present":
            raise ValueError("Snapshot root entry is missing")
        for entry, value in entries.items():
            if not isinstance(entry, str) or not isinstance(value, str):
                raise ValueError("Invalid snapshot entry type")
            kind, separator, identifier = entry.partition("/")
            if separator != "/" or HEX_DIGEST.fullmatch(identifier) is None:
                raise ValueError("Invalid snapshot entry identifier")
            if kind == root_kind and value == "present":
                continue
            content_kind = "file" if group in TREE_GROUPS else "value"
            if kind != content_kind or SIZED_DIGEST.fullmatch(value) is None:
                raise ValueError("Invalid snapshot entry value")
            if kind == "value" and int(value.split(":", 1)[0]) not in range(12):
                raise ValueError("Unsupported registry value kind")


def compare(before: dict, after: dict) -> dict:
    validate_snapshot(before)
    validate_snapshot(after)
    if before["identity"] != after["identity"]:
        raise ValueError("Snapshot identity/schema mismatch")
    changes = []
    for group in sorted(set(before["groups"]) | set(after["groups"])):
        left, right = before["groups"].get(group, {}), after["groups"].get(group, {})
        for key in sorted(set(left) | set(right)):
            if left.get(key) != right.get(key):
                changes.append({"group": group, "id": key, "change": "added" if key not in left else "removed" if key not in right else "changed"})
    return {"schema": 1, "preserved": not changes, "changeCount": len(changes), "changes": changes, "exemptions": []}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=str(Path(__file__).resolve().parents[2]))
    sub = parser.add_subparsers(dest="command", required=True)
    snapshot = sub.add_parser("snapshot")
    snapshot.add_argument("--out", required=True)
    diff = sub.add_parser("compare")
    diff.add_argument("--before", required=True)
    diff.add_argument("--after", required=True)
    diff.add_argument("--out", required=True)
    args = parser.parse_args()
    try:
        repo = Path(args.repo_root).resolve()
        out = output_path(repo, args.out)
        if args.command == "snapshot":
            report = capture(repo)
            validate_snapshot(report)
            write_new(out, report)
            print(json.dumps({"captured": True, "groups": {name: len(entries) for name, entries in report["groups"].items()}}))
            return 0
        report = compare(json.loads(Path(args.before).read_text(encoding="utf-8")), json.loads(Path(args.after).read_text(encoding="utf-8")))
        write_new(out, report)
        print(json.dumps({"preserved": report["preserved"], "changeCount": report["changeCount"]}))
        return 0 if report["preserved"] else 1
    except Exception as error:
        # Do not echo exception text: OS messages can include private file/key names.
        print(json.dumps({"completed": False, "errorType": type(error).__name__}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
