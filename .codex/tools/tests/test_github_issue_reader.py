from __future__ import annotations

import sys
import unittest
from pathlib import Path
from urllib.parse import urlparse

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from github_issue_reader import CommandResult, IssueReadError, IssueReader, hook_context


def issue(number: int, title: str = "Task", body: str | None = "Instructions") -> dict:
    return {
        "number": number,
        "title": title,
        "body": body,
        "html_url": f"https://github.com/kissho222/Nekolpos/issues/{number}",
        "labels": [{"name": "codex-task"}],
        "created_at": "2026-09-19T00:00:00Z",
        "updated_at": "2026-09-19T01:00:00Z",
        "state": "open",
    }


class ReaderTests(unittest.TestCase):
    def reader(self, pages: dict[str, object]) -> IssueReader:
        def command(arguments: list[str]) -> CommandResult:
            if arguments[:4] == ["git", "remote", "get-url", "origin"]:
                return CommandResult(0, "https://github.com/kissho222/Nekolpos.git\n")
            raise AssertionError(arguments)

        def http_get(url: str):
            path = urlparse(url).path
            response = pages.get(path)
            if isinstance(response, Exception):
                raise response
            return response if response is not None else []

        return IssueReader(command_runner=command, http_getter=http_get, which=lambda _: None)

    def test_no_candidates_continues_normally(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues": []}).read("")
        self.assertEqual("no_candidates", result["status"])

    def test_one_candidate_includes_instruction_body(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues": [issue(12, "位置Condition", "実装する")]}).read("")
        self.assertEqual("selected", result["status"])
        self.assertEqual(12, result["issue"]["number"])
        self.assertIn("実装する", hook_context(result))

    def test_multiple_candidates_are_not_selected(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues": [issue(12), issue(13)]}).read("")
        self.assertEqual("multiple_candidates", result["status"])
        self.assertNotIn("issue", result)

    def test_explicit_issue_number_fetches_that_issue(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues/12": issue(12)}).read("Issue #12を対応して")
        self.assertEqual("selected", result["status"])
        self.assertEqual(12, result["issue"]["number"])

    def test_japanese_task_number_fetches_that_issue(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues/12": issue(12)}).read("タスク12実行")
        self.assertEqual("selected", result["status"])
        self.assertEqual(12, result["issue"]["number"])

    def test_authentication_failure_is_safe(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues": IssueReadError("HTTP 401 token=secret")}).read("")
        self.assertEqual("issue_fetch_failed", result["status"])
        self.assertNotIn("secret", hook_context(result))

    def test_network_failure_is_safe(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues": IssueReadError("network disconnected")}).read("")
        self.assertEqual("issue_fetch_failed", result["status"])
        self.assertIn("安全に取得できませんでした", hook_context(result))

    def test_empty_body_is_preserved_as_empty(self) -> None:
        result = self.reader({"/repos/kissho222/Nekolpos/issues": [issue(12, body=None)]}).read("")
        self.assertEqual("", result["issue"]["body"])
        self.assertIn("本文なし", hook_context(result))

    def test_pull_request_is_not_a_candidate(self) -> None:
        pull_request = issue(12)
        pull_request["pull_request"] = {"url": "https://api.github.com/repos/kissho222/Nekolpos/pulls/12"}
        result = self.reader({"/repos/kissho222/Nekolpos/issues": [pull_request]}).read("")
        self.assertEqual("no_candidates", result["status"])


if __name__ == "__main__":
    unittest.main()
