# SE 基盤セットアップのメモリー検証

Python 3.10 以上と .NET SDK 10 が必要。追加 NuGet package は使わない。Unity、実音、通常保存、PlayerPrefs、EditorPrefs、Registry、実 Scene／Prefab／AssetDatabase は使わない。

PR を checkout したリポジトリのルートで実行する。

```text
python .codex/tools/sound_effects_setup_checks/verify.py
```

別 checkout は `--repo-root <checkout>`、出力先は `--output <repo/output配下の新directory>` で指定できる。出力先は未作成のディレクトリだけを受け付け、過去の結果を上書きしない。既定は `output/sound-effects-setup-checks/<UTC日時-ID>/`。生成 project、bin／obj、SDK のユーザー領域、NuGet cache、ログ、結果はこの新規フォルダー内に置く。外部 package source は空にする。

対象 checkout の `Assets/Scripts/Editor/SoundEffectsSetupActions.cs` 全文を直接参照してコンパイルする。本文の抽出・書き換えや過去の候補ファイルへの置き換えはしない。`Program.cs` 内の Unity／Editor と関連 component の代役はメモリー内だけで動作する。helper 内のファイル存在確認も新規出力ディレクトリを基準に実行するため、通常プロジェクトの Assets に対する authoring は行わない。

44 ケースで不足 component の追加、既存の非空／空／null 値の保持、繰返し no-op、非 active 親の床材、Undo／途中失敗の巻戻し、曖昧・不正な対象の拒否、空 Profile の新規作成／既存 Profile の再利用、OP 猫と通常猫の区別、bootstrap の通常猫参照優先を確認する。単独 Profile asset は成功後の component Undo では削除しない契約も確認する。ケースを増減した場合は `EXPECTED_COUNT` と仕様を一緒に更新する。

今回結果は `verification-manifest.json`、各ケースは `results.json`、工程別出力は `*.stdout.txt`／`*.stderr.txt` に保存する。manifest は開始時 `completed=false / passed=false`、全44ケースと source／tool の前後 SHA-256 一致を確認した正常終端だけ `completed=true` にする。成功条件は終了値0、今回 manifest の `completed=true / passed=true`、44ケースの全成功。終了値1は検証不一致、2は環境／準備／compile等の未完了を表す。実helperの証跡コピーは `helper-source-snapshot.cs.txt` に残すが、コンパイル入力には使わない。

これは **管理コード上の限定検証**。代役の Undo、検索、シリアライズ、Prefab override、AssetDatabase、inactive 階層は Unity の全挙動を再現しない。Native の参照同一性、実 Undo stack、import、Scene dirty／保存、実 component lifecycle、音源、聴感、実際の Scene 配線は合格の対象外。実 Unity の限定検証や既存 Scene の読取確認は、別の証跡として扱う。

検証ツール自体の失敗処理は `python .codex/tools/sound_effects_setup_checks/test_verify.py` で確認する。最終manifest保存と成功通知の標準出力にそれぞれ例外を注入し、`completed / passed` がともにfalseとなる2件の合成検証。SDK処理は代役に置き換え、製品44ケースは再実行しない。結果と合成入力は新規 `output/sound-effects-setup-checks-tool-tests/synthetic-*/` に保存する。
