# Unityroom Reporter の外部検証

Python 3.10 以上と .NET SDK 10.0 が必要。追加パッケージ、Unity、ネットワーク接続は使わない。

リポジトリのルートで実行する。

```text
python .codex/tools/unityroom_reporter_checks/verify.py
```

別 checkout は `--repo-root <checkout>`、出力先は `--output <directory>` で指定できる。現在の `Assets/Scripts/System/UnityRoomTeachingRankingReporter.cs` 全文を直接コンパイルし、Unity と unityroom の実装をメモリー上の代役へ置き換える。製品コードを生成・書換しない。

Windows Release / Development、WebGL Editor / Release / Development の 5 条件で各 18 ケースを確認する。手動送信、起動、発見通知、リセット、通知解除、送信設定、ボード番号、モード、クライアント不在を対象とし、対象外プラットフォーム・隔離状態では `Client.Instance` にも到達しないことを確認する。通常 WebGL のスコア・ボード・送信モードは維持する。

負の対照には、既知の Git commit `930b607317b1755aa3c9070855cec3301e6b9a7c` の未修正 Reporter を `git show` で読む。fetch は行わない。別の未修正ソースを使う場合は `--baseline <file>`、ローカル Git ref を使う場合は `--baseline-ref <ref>`。旧コードが Native/Editor の送信と隔離 WebGL の送信で期待どおり失敗しなければ、検証全体を失敗扱いとする。

既定の生成先は `output/unityroom-reporter-checks/`。`verification-manifest.json` にソース・テスト・旧ソースの SHA-256、SDK、条件別結果と検証前後のソース一致を記録し、条件別の `result.stdout.txt` に各ケースを残す。終了値は 0=成功、1=期待と異なる挙動、2=環境不足や実行不能。

開始時に manifest を `status=incomplete / completed=false / passed=false` へ更新する。SDK・旧ソースの不足や途中のコンパイル失敗ではこの状態を残し、前回の成功を今回の成功として表示しない。成功判定は今回の終了値0と `completed=true / passed=true` の両方を使う。過去の結果を保持する場合は毎回別の `--output` を指定する。

監査ツール自身の破損入力・途中失敗処理は `python .codex/tools/audit_tool_checks.py` で合成データだけを使って確認できる。この自己検査は Registry・通常保存を読まず、Unity・dotnet を起動しない。

これは Reporter の振る舞いと reflection 呼出しの検証であり、Unity package、アセット、実通信、IL2CPP、実 Player の合格を意味しない。Release の実 `DebugSaveSession.IsIsolated` は常に false。Release 条件の isolated=true ケースは同じ分岐の安全方針を確認するためのテスト用代役である。通常 PlayerPrefs・保存ファイル・Unity DLL は読み書き・実行しない。
