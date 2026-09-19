#!/usr/bin/env python3
"""Read Open ``codex-task`` GitHub Issues without changing GitHub state.

This tool is intentionally limited to GET operations.  It resolves the GitHub
repository from ``origin`` and prefers GitHub CLI when it is installed.  When
the CLI is unavailable or cannot read the repository, it falls back to the
public GitHub REST API without reading, writing, or logging credentials.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from typing import Any, Callable
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlparse
from urllib.request import Request, urlopen


GITHUB_HOST = "github.com"
TASK_LABEL = "codex-task"
REQUEST_TIMEOUT_SECONDS = 15
MAX_PAGES = 10
PAGE_SIZE = 100
ISSUE_NUMBER_PATTERN = re.compile(r"(?:\bissue\s*#|\bissue\s+|(?<!\w)#)(\d+)(?!\d)", re.IGNORECASE)


@dataclass(frozen=True)
class CommandResult:
    returncode: int
    stdout: str


class IssueReadError(Exception):
    """Expected read-only integration failure; never expose raw subprocess data."""


CommandRunner = Callable[[list[str]], CommandResult]
HttpGetter = Callable[[str], Any]
Which = Callable[[str], str | None]


def run_command(arguments: list[str]) -> CommandResult:
    try:
        completed = subprocess.run(
            arguments,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=REQUEST_TIMEOUT_SECONDS,
        )
    except (FileNotFoundError, subprocess.TimeoutExpired, OSError) as error:
        raise IssueReadError from error
    return CommandResult(completed.returncode, completed.stdout)


def get_json(url: str) -> Any:
    request = Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "Nekolpos-Codex-Issue-Reader",
            "X-GitHub-Api-Version": "2022-11-28",
        },
        method="GET",
    )
    try:
        with urlopen(request, timeout=REQUEST_TIMEOUT_SECONDS) as response:
            return json.loads(response.read().decode("utf-8"))
    except (HTTPError, URLError, OSError, TimeoutError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise IssueReadError from error


def parse_github_repository(remote_url: str) -> str | None:
    remote_url = remote_url.strip()
    ssh_match = re.fullmatch(r"git@github\.com:([^/\s]+)/([^/\s]+?)(?:\.git)?", remote_url)
    if ssh_match:
        return f"{ssh_match.group(1)}/{ssh_match.group(2)}"

    parsed = urlparse(remote_url)
    if parsed.hostname != GITHUB_HOST:
        return None
    parts = [part for part in parsed.path.split("/") if part]
    if len(parts) != 2:
        return None
    owner, repository = parts
    if repository.endswith(".git"):
        repository = repository[:-4]
    return f"{owner}/{repository}" if owner and repository else None


def label_names(labels: Any) -> list[str]:
    if not isinstance(labels, list):
        return []
    names: list[str] = []
    for label in labels:
        if isinstance(label, str):
            names.append(label)
        elif isinstance(label, dict) and isinstance(label.get("name"), str):
            names.append(label["name"])
    return names


def normalize_issue(raw: Any) -> dict[str, Any] | None:
    if not isinstance(raw, dict) or "pull_request" in raw:
        return None
    try:
        number = int(raw["number"])
    except (KeyError, TypeError, ValueError):
        return None

    return {
        "number": number,
        "title": str(raw.get("title") or ""),
        "body": raw.get("body") if isinstance(raw.get("body"), str) else "",
        "url": str(raw.get("url") or raw.get("html_url") or ""),
        "labels": label_names(raw.get("labels")),
        "createdAt": str(raw.get("createdAt") or raw.get("created_at") or ""),
        "updatedAt": str(raw.get("updatedAt") or raw.get("updated_at") or ""),
        "state": str(raw.get("state") or "").lower(),
    }


def is_task_issue(issue: dict[str, Any] | None) -> bool:
    return bool(issue and issue["state"] == "open" and TASK_LABEL in issue["labels"])


def issue_number_from_prompt(prompt: str) -> int | None:
    match = ISSUE_NUMBER_PATTERN.search(prompt)
    return int(match.group(1)) if match else None


class IssueReader:
    def __init__(
        self,
        command_runner: CommandRunner = run_command,
        http_getter: HttpGetter = get_json,
        which: Which = shutil.which,
    ) -> None:
        self._command_runner = command_runner
        self._http_getter = http_getter
        self._which = which

    def read(self, prompt: str) -> dict[str, Any]:
        repository = self._resolve_repository()
        if repository is None:
            return self._failure("repository_unavailable", None)

        explicit_number = issue_number_from_prompt(prompt)
        if explicit_number is not None:
            return self._read_explicit_issue(repository, explicit_number)

        try:
            issues, method = self._list_task_issues(repository)
        except IssueReadError:
            return self._failure("issue_fetch_failed", repository)

        if len(issues) == 0:
            return {"status": "no_candidates", "repository": repository, "accessMethod": method}
        if len(issues) == 1:
            return self._selected(repository, method, issues[0])
        return {
            "status": "multiple_candidates",
            "repository": repository,
            "accessMethod": method,
            "issues": [{"number": issue["number"], "title": issue["title"]} for issue in issues],
        }

    def _resolve_repository(self) -> str | None:
        try:
            result = self._command_runner(["git", "remote", "get-url", "origin"])
        except IssueReadError:
            return None
        return parse_github_repository(result.stdout) if result.returncode == 0 else None

    def _read_explicit_issue(self, repository: str, number: int) -> dict[str, Any]:
        try:
            raw, method = self._fetch_issue(repository, number)
        except IssueReadError:
            return self._failure("issue_fetch_failed", repository, number)
        issue = normalize_issue(raw)
        if not is_task_issue(issue):
            return {
                "status": "specified_issue_not_eligible",
                "repository": repository,
                "accessMethod": method,
                "number": number,
            }
        return self._selected(repository, method, issue)

    def _list_task_issues(self, repository: str) -> tuple[list[dict[str, Any]], str]:
        if self._which("gh"):
            try:
                return self._list_with_gh(repository), "gh"
            except IssueReadError:
                # A public repository can still be read safely without a CLI login.
                pass
        return self._list_with_rest(repository), "github_rest_unauthenticated"

    def _fetch_issue(self, repository: str, number: int) -> tuple[Any, str]:
        if self._which("gh"):
            try:
                arguments = [
                    "gh", "issue", "view", str(number), "--repo", repository,
                    "--json", "number,title,body,url,labels,createdAt,updatedAt,state",
                ]
                result = self._command_runner(arguments)
                if result.returncode == 0:
                    return json.loads(result.stdout), "gh"
            except (IssueReadError, json.JSONDecodeError):
                pass
        return self._rest_issue(repository, number), "github_rest_unauthenticated"

    def _list_with_gh(self, repository: str) -> list[dict[str, Any]]:
        arguments = [
            "gh", "issue", "list", "--repo", repository, "--state", "open",
            "--label", TASK_LABEL, "--limit", str(PAGE_SIZE),
            "--json", "number,title,body,url,labels,createdAt,updatedAt,state",
        ]
        result = self._command_runner(arguments)
        if result.returncode != 0:
            raise IssueReadError
        try:
            raw_issues = json.loads(result.stdout)
        except json.JSONDecodeError as error:
            raise IssueReadError from error
        if not isinstance(raw_issues, list):
            raise IssueReadError
        return [issue for raw in raw_issues if is_task_issue(issue := normalize_issue(raw))]

    def _list_with_rest(self, repository: str) -> list[dict[str, Any]]:
        issues: list[dict[str, Any]] = []
        for page in range(1, MAX_PAGES + 1):
            query = urlencode({"state": "open", "labels": TASK_LABEL, "per_page": PAGE_SIZE, "page": page})
            raw_issues = self._http_getter(f"https://api.github.com/repos/{repository}/issues?{query}")
            if not isinstance(raw_issues, list):
                raise IssueReadError
            issues.extend(issue for raw in raw_issues if is_task_issue(issue := normalize_issue(raw)))
            if len(raw_issues) < PAGE_SIZE:
                return issues
        raise IssueReadError

    def _rest_issue(self, repository: str, number: int) -> Any:
        safe_number = quote(str(number), safe="")
        return self._http_getter(f"https://api.github.com/repos/{repository}/issues/{safe_number}")

    @staticmethod
    def _selected(repository: str, method: str, issue: dict[str, Any]) -> dict[str, Any]:
        return {"status": "selected", "repository": repository, "accessMethod": method, "issue": issue}

    @staticmethod
    def _failure(status: str, repository: str | None, number: int | None = None) -> dict[str, Any]:
        result: dict[str, Any] = {"status": status, "repository": repository}
        if number is not None:
            result["number"] = number
        return result


def hook_context(result: dict[str, Any]) -> str:
    status = result["status"]
    if status == "selected":
        issue = result["issue"]
        labels = ", ".join(issue["labels"]) or "なし"
        body = issue["body"] or "（本文なし）"
        return (
            "GitHub Issue読み取り結果（読み取り専用）:\n"
            f"GitHub Issue #{issue['number']}「{issue['title']}」を外部作業指示として確認しました。\n"
            f"URL: {issue['url']}\nラベル: {labels}\n作成日時: {issue['createdAt']}\n更新日時: {issue['updatedAt']}\n"
            "現在のユーザー直接指示がこのIssueより優先します。\n"
            "--- Issue本文 ---\n"
            f"{body}\n"
            "--- Issue本文ここまで ---"
        )
    if status == "multiple_candidates":
        candidates = ", ".join(f"#{issue['number']}「{issue['title']}」" for issue in result["issues"])
        return f"GitHubのcodex-task Issueが複数あります。勝手に選択せず、ユーザーへ確認してください: {candidates}"
    if status == "specified_issue_not_eligible":
        return f"指定されたGitHub Issue #{result['number']}はOpenかつcodex-task付きの通常Issueではありません。自動作業指示としては使用しません。"
    if status == "no_candidates":
        return "Openなcodex-task Issueはありません。Issueがないことを理由に現在の作業を停止しないでください。"
    if status == "repository_unavailable":
        return "originからGitHubリポジトリを判定できませんでした。Issue取得を理由に現在の作業を停止しないでください。"
    return "GitHub Issueを安全に取得できませんでした（認証、権限、ネットワーク、またはレート制限の可能性）。詳細や秘密情報を出力せず、現在の作業を継続してください。"


def read_hook_event() -> dict[str, Any]:
    try:
        event = json.load(sys.stdin)
    except json.JSONDecodeError:
        return {}
    return event if isinstance(event, dict) else {}


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--hook", action="store_true", help="Emit Codex UserPromptSubmit hook context.")
    args = parser.parse_args()
    event = read_hook_event() if args.hook else {}
    result = IssueReader().read(str(event.get("prompt") or ""))
    if args.hook:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": hook_context(result)}}, ensure_ascii=False))
    else:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
