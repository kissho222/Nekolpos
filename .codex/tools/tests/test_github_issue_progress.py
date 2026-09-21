from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from github_issue_progress import IssueProgress, ProgressError
from github_issue_reader import CommandResult, IssueReader


def reader() -> IssueReader:
    return IssueReader(
        command_runner=lambda _: CommandResult(0, "https://github.com/kissho222/Nekolpos.git\n"),
        http_getter=lambda _: [],
        which=lambda _: None,
    )


class ProgressTests(unittest.TestCase):
    def progress(self, labels: list[str]) -> tuple[IssueProgress, list[tuple[str, str, dict | None]]]:
        calls: list[tuple[str, str, dict | None]] = []

        def request(method: str, url: str, payload: str | None, _: str):
            body = json.loads(payload) if payload else None
            calls.append((method, url, body))
            if method == "GET" and "/issues/" in url:
                return {"state": "open", "labels": [{"name": label} for label in labels]}
            return {"ok": True}

        return IssueProgress(token="test-token", reader=reader(), http_request=request), calls

    def test_start_replaces_task_label_and_preserves_other_labels(self) -> None:
        progress, calls = self.progress(["codex-task", "priority"])
        progress.transition(5, "start")
        update = next(call for call in calls if call[0] == "PUT")
        self.assertEqual({"labels": ["priority", "codex-working"]}, update[2])

    def test_complete_posts_report_before_review_label(self) -> None:
        progress, calls = self.progress(["codex-working"])
        report = "\n".join(("### 変更ファイル", "a", "### 実装内容", "b", "### ビルド/テスト結果", "c", "### ユーザー側で確認してほしい点", "d"))
        progress.transition(5, "complete", report)
        methods = [call[0] for call in calls]
        self.assertLess(methods.index("POST"), methods.index("PUT"))
        update = next(call for call in calls if call[0] == "PUT")
        self.assertEqual({"labels": ["codex-review"]}, update[2])

    def test_completion_requires_the_four_report_sections(self) -> None:
        progress, _ = self.progress(["codex-working"])
        with self.assertRaises(ProgressError):
            progress.transition(5, "complete", "### 変更ファイル")

    def test_close_can_remove_review_label(self) -> None:
        progress, calls = self.progress(["codex-review", "priority"])
        progress.transition(5, "close", remove_review=True)
        update = next(call for call in calls if call[0] == "PUT")
        self.assertEqual({"labels": ["priority"]}, update[2])
        self.assertEqual("PATCH", calls[-1][0])

    def test_uses_authenticated_cli_token_when_environment_tokens_are_absent(self) -> None:
        received_tokens: list[str] = []

        def request(method: str, url: str, payload: str | None, token: str):
            received_tokens.append(token)
            if method == "GET" and "/issues/" in url:
                return {"state": "open", "labels": [{"name": "codex-task"}]}
            return {"ok": True}

        progress = IssueProgress(
            reader=reader(),
            http_request=request,
            token_resolver=lambda: "keyring-token",
        )

        progress.transition(7, "start")

        self.assertEqual(["keyring-token"] * len(received_tokens), received_tokens)


if __name__ == "__main__":
    unittest.main()
