# 開発引き継ぎ

> 現在状態のみを記録する。秘密情報、APIキー、個人情報、会話・Issueの全文は記載しない。

## 更新日時

2026-09-19 14:56:59 JST

## 現在の作業

GitHubの`codex-task` Issueを読み取り専用で取得し、Codexの作業指示へ連携する基盤の追加（完了）。

## 現在の状態

`UserPromptSubmit` HookでOpenかつ`codex-task`ラベル付きの通常Issueを取得し、作業開始コンテキストへ渡せる状態。`gh`は未導入のため、公開リポジトリでは認証情報を使わないGitHub REST APIのGETへフォールバックする。実環境では対象Issueが0件だった。Unityプロジェクト本体は未変更。

## 完了したこと

- 現在状態だけを保持するHANDOFFテンプレートを追加した。
- Codexの恒久運用ルールを`AGENTS.md`へ追加した。
- Codex Hooksの現行仕様と、この環境での利用条件を確認し、Stop Hookのガードを追加した。
- `kissho222/Nekolpos`が公開リポジトリであり、現時点で空であることを確認した。
- ローカルGitリポジトリを初期化し、`origin`のfetch/push URLを`https://github.com/kissho222/Nekolpos.git`へ設定した。
- 初回コミット`7f027bb`を`origin/chore/ai-handoff-foundation`へpushし、リモートブランチの存在を確認した。
- `github_issue_reader.py`を追加し、`origin`からリポジトリを判定して、Open・`codex-task`・非Pull RequestのIssueだけを読み取れるようにした。
- Issueが1件なら本文を含む作業指示コンテキストを渡し、0件なら通常作業を続行し、複数件なら選択せずユーザー確認を求めるようにした。
- 現在のユーザー直接指示をIssueより優先し、Issueの変更・close・コメント投稿などを行わないルールを`AGENTS.md`へ追加した。
- `gh`が未導入であることを確認し、GitHub REST APIの読み取りでOpenな`codex-task` Issueが0件であることを確認した。
- Unityプロジェクト本体は変更していない。

## 変更した主要ファイル

- `AGENTS.md`
- `.ai/HANDOFF.md`
- `.codex/hooks.json`
- `.codex/hooks/handoff_guard.py`
- `.codex/tools/github_issue_reader.py`
- `.codex/tools/tests/test_github_issue_reader.py`
- `.gitignore`

## 実施した動作確認・テスト

- `handoff_guard.py`のPython構文を解析して確認した。
- `.codex/hooks.json`のJSON構文を確認した。
- 更新対象がない通常のStopで通過することを確認した。
- HANDOFFより後にプロジェクト側の更新がある場合、Stopが続行要求を返すことを確認した。
- 子ディレクトリをカレントディレクトリにしたWindowsコマンドでも、Hookがワークスペースを検出して動作することを確認した。
- 初回コミット前に、ステージング対象が引き継ぎ基盤の4ファイルだけであることと、差分の空白エラーがないことを確認した。
- `git push -u origin chore/ai-handoff-foundation`の成功後、`git ls-remote`でリモートブランチを確認した。
- Issue取得ツールの振る舞いテスト8件（0件、1件、複数件、明示番号、認証失敗、ネットワーク失敗、本文空、Pull Request除外）を実行して成功した。
- Windowsの`UserPromptSubmit` Hookコマンドを実行し、実環境の0件結果をCodex用JSONコンテキストとして取得した。
- `github_issue_reader.py`のPython構文と更新後の`hooks.json`構文を確認した。

## 未解決事項

- `gh`が未導入のため、非公開リポジトリへ変更した場合は、ユーザーが`gh`を導入・認証するか、別途認可済みの読み取り手段を用意する必要がある。取得失敗時も直接指示による通常作業は継続できる。

## 今回確定した仕様・判断

- HANDOFFは指定の9項目だけを現在状態として更新し、履歴を追記しない。
- Issue取得はStop Hookから分離した`.codex/tools/github_issue_reader.py`が担い、`UserPromptSubmit` Hookから毎回読み取り専用で実行する。
- `gh`が利用可能なら優先し、利用不可または読み取り失敗時は、秘密情報を使わないGitHub REST API GETを1回ずつ試す。無限リトライはしない。
- REST APIのIssue一覧にはPull Requestも含まれ得るため、`pull_request`フィールドを持つ要素は候補から除外する。
- Issue本文はHookの作業開始コンテキストとしてのみ使い、HANDOFFへ全文を転記しない。
- `.codex/tools`配下のテスト実行で生成されるPythonバイトコードだけを`.gitignore`で除外する。Unityの生成物や既存ファイルはこの設定で除外しない。
- Stop Hookは通常の更新を代替せず、同一ターンでプロジェクトファイルが更新されたのにHANDOFFがその後更新されていない場合だけ、1回だけ続行を促す。
- Hookの状態はOSの一時ディレクトリにのみ保存し、秘密情報・会話本文・Issue本文を保存しない。
- 初回pushは`main`へ直接コミットせず、`chore/ai-handoff-foundation`ブランチを使用する。

## 次に行う候補

- GitHubでOpenかつ`codex-task`ラベル付きIssueを作成し、新しいCodex作業を開始して自動取得を利用する。
- 必要になった時点で、初回ブランチのPull Request作成または既定ブランチ設定を行う。
