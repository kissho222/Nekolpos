# Debug Scenario Runnerの外部メモリー検証

Python 3.10以上と.NET SDK 10.0が必要。追加NuGetパッケージは使わない。

リポジトリルートから次を実行する。

```powershell
python .codex/tools/debug_scenario_runner_checks/verify.py
```

別checkoutを検査する場合のみ `--repo-root <checkout>` を指定できる。現行の `Assets/Scripts/Editor/DebugScenarioEditorRunner.cs` から、境界コメント付き純粋helperとRejectをそのまま抽出し、メモリー上のclock/task/SessionState/Saveで検証する。加えて `DebugConsoleInput.cs` と `ChatUIController.cs` の入力ガードと送りメソッドを抽出し、Runner実行中の通常入力遮断と専用送りを別実行で検証する。抽出境界が変更された場合は環境不足として停止し、古い抽出コードを流用しない。

生成物はGit無視対象の `output/debug-scenario-runner-checks/`。`memory-only-results.json` と `input-results.json` はケース別結果、`verification-manifest.json` はsource/helper/harness SHA-256・SDK・実行範囲、`build-run.log` は外部compile実行ログ。実行前後の3ソース一致も検査する。終了値は0=成功、1=検証失敗、2=環境不足/抽出不能。

取消、60秒期限を模した手動clock、gate、同フレーム完了、遅延fault、batchの開始拒否/例外/終了失敗/予約後clearを検査する。入力検査はConsole所有/lease/closing-frame、隔離・scenario実行状態、menu/delayed/typing/choiceと通常挙動の一致を確認する。`legacy_reject.fixture.cs`、`LegacyBatch.cs`、`legacy_input.fixture.cs` は修正前の最小negative controlであり、製品へ組み込まない。実装原文のhelperが正例で、旧不具合が再現できることも確認する。現在はRunner22件＋入力14件である。

Unity起動、Play、正式NUnit、worker、実保存adapter、PlayerPrefs、Scene保存は行わない。検証結果はUnity compile、日記UI統合、通常保存保全、実Playerの合格を意味しない。製品Runnerやデータを書き換えず、手動操作や実Playの承認にも代用しない。
