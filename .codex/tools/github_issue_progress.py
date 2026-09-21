#!/usr/bin/env python3
"""Transition Codex GitHub Issue progress labels without exposing credentials.

This command is intentionally scoped to the repository resolved from ``origin``.
It preserves unrelated labels, creates the two standard progress labels when they
are missing, and posts a validated completion report before marking an Issue as
ready for review.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen

from github_issue_reader import IssueReadError, IssueReader


API_ROOT = "https://api.github.com"
PROGRESS_LABELS = {
    "codex-working": {"color": "1D76DB", "description": "Codex is implementing this task."},
    "codex-review": {"color": "8250DF", "description": "Codex implementation is ready for user review."},
}
REQUIRED_REPORT_HEADINGS = (
    "### 変更ファイル",
    "### 実装内容",
    "### ビルド/テスト結果",
    "### ユーザー側で確認してほしい点",
)


class ProgressError(Exception):
    """Expected failure which must not reveal credentials or raw API output."""


HttpRequest = Callable[[str, str, str | None, str], Any]
TokenResolver = Callable[[], str | None]


def resolve_authentication_token() -> str | None:
    """Prefer explicit tokens, then use an authenticated GitHub CLI keyring entry."""
    environment_token = os.getenv("GITHUB_TOKEN") or os.getenv("GH_TOKEN")
    if environment_token:
        return environment_token

    github_cli = shutil.which("gh")
    if github_cli is None:
        return None

    try:
        completed = subprocess.run(
            [github_cli, "auth", "token"],
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=15,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None

    token = completed.stdout.strip() if completed.returncode == 0 else ""
    return token or None


def request_json(method: str, url: str, payload: str | None, token: str) -> Any:
    request = Request(
        url,
        data=payload.encode("utf-8") if payload is not None else None,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "User-Agent": "Nekolpos-Codex-Issue-Progress",
            "X-GitHub-Api-Version": "2022-11-28",
        },
        method=method,
    )
    try:
        with urlopen(request, timeout=15) as response:
            data = response.read().decode("utf-8")
    except (HTTPError, URLError, OSError, TimeoutError, UnicodeDecodeError) as error:
        raise ProgressError from error

    if not data:
        return None
    try:
        return json.loads(data)
    except json.JSONDecodeError as error:
        raise ProgressError from error


class IssueProgress:
    def __init__(
        self,
        token: str | None = None,
        reader: IssueReader | None = None,
        http_request: HttpRequest = request_json,
        token_resolver: TokenResolver = resolve_authentication_token,
    ) -> None:
        self._token = token if token is not None else token_resolver()
        self._reader = reader or IssueReader()
        self._http_request = http_request

    def transition(self, issue_number: int, action: str, report: str | None = None, remove_review: bool = False) -> None:
        if issue_number <= 0:
            raise ProgressError
        if not self._token:
            raise ProgressError

        repository = self._reader._resolve_repository()
        if repository is None:
            raise ProgressError

        issue = self._request("GET", f"/repos/{repository}/issues/{issue_number}")
        if not isinstance(issue, dict) or "pull_request" in issue or str(issue.get("state", "")).lower() != "open":
            raise ProgressError
        labels = self._label_names(issue.get("labels"))

        if action == "start":
            self._require_transition(labels, "codex-task", "codex-working")
            self._ensure_progress_labels(repository)
            self._replace_labels(repository, issue_number, self._replace(labels, {"codex-task", "codex-review"}, "codex-working"))
            return

        if action == "complete":
            self._require_transition(labels, "codex-working", "codex-review")
            if report is None or not self.is_valid_report(report):
                raise ProgressError
            self._ensure_progress_labels(repository)
            self._request("POST", f"/repos/{repository}/issues/{issue_number}/comments", json.dumps({"body": report}, ensure_ascii=False))
            self._replace_labels(repository, issue_number, self._replace(labels, {"codex-task", "codex-working"}, "codex-review"))
            return

        if action == "close":
            if "codex-review" not in labels:
                raise ProgressError
            if remove_review:
                self._replace_labels(repository, issue_number, [label for label in labels if label != "codex-review"])
            self._request("PATCH", f"/repos/{repository}/issues/{issue_number}", json.dumps({"state": "closed"}))
            return

        raise ProgressError

    @staticmethod
    def is_valid_report(report: str) -> bool:
        return all(heading in report for heading in REQUIRED_REPORT_HEADINGS)

    def _request(self, method: str, path: str, payload: str | None = None) -> Any:
        return self._http_request(method, f"{API_ROOT}{path}", payload, self._token or "")

    def _ensure_progress_labels(self, repository: str) -> None:
        for name, definition in PROGRESS_LABELS.items():
            try:
                self._request("GET", f"/repos/{repository}/labels/{quote(name, safe='')}")
            except ProgressError:
                self._request("POST", f"/repos/{repository}/labels", json.dumps({"name": name, **definition}))

    def _replace_labels(self, repository: str, issue_number: int, labels: list[str]) -> None:
        self._request("PUT", f"/repos/{repository}/issues/{issue_number}/labels", json.dumps({"labels": labels}))

    @staticmethod
    def _label_names(raw_labels: Any) -> list[str]:
        names: list[str] = []
        if not isinstance(raw_labels, list):
            return names
        for label in raw_labels:
            if isinstance(label, str):
                names.append(label)
            elif isinstance(label, dict) and isinstance(label.get("name"), str):
                names.append(label["name"])
        return names

    @staticmethod
    def _require_transition(labels: list[str], source: str, target: str) -> None:
        if source not in labels and target not in labels:
            raise ProgressError

    @staticmethod
    def _replace(labels: list[str], remove: set[str], add: str) -> list[str]:
        return [label for label in labels if label not in remove] + ([] if add in labels else [add])


def read_report(path: str | None) -> str:
    if not path:
        raise ProgressError
    try:
        return Path(path).read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        raise ProgressError from error


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for action in ("start", "complete", "close"):
        action_parser = actions.add_parser(action)
        action_parser.add_argument("--issue", type=int, required=True)
        if action == "complete":
            report_source = action_parser.add_mutually_exclusive_group(required=True)
            report_source.add_argument("--report-file")
            report_source.add_argument("--report-stdin", action="store_true")
        if action == "close":
            action_parser.add_argument("--remove-review", action="store_true")
    arguments = parser.parse_args()

    try:
        report = None
        if arguments.action == "complete":
            report = sys.stdin.read() if arguments.report_stdin else read_report(arguments.report_file)
        IssueProgress().transition(arguments.issue, arguments.action, report, getattr(arguments, "remove_review", False))
    except (ProgressError, IssueReadError):
        print("GitHub Issueの進捗更新に失敗しました。認証・権限・Issue状態・完了報告を確認してください。", file=sys.stderr)
        return 1

    print("GitHub Issueの進捗を更新しました。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
