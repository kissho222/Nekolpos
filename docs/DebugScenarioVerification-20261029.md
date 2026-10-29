# Issue #80 現行シナリオの再検証（2026-10-29）

現行checkoutの既存Runnerを再利用し、8IDの自動状態検証・正式なTable復帰・通常保存/Assets/Scene保全を確認した。最終修正版で実行したのは通常日記と特別日記（各15チェック）、normal.tableの回帰（13チェック）の3ID。normal.desk、transition.table-desk、walk、tablet.desk、death.respawnの5ID（各13チェック）は入力修正前の実績であり、「最終版で8ID合格」とは扱わない。各成功runの前後9,113項目は差分0。Play停止、隔離解除、Runner Pending/Batch空まで確認した。

日記2件はカレンダー確定、Pending消費、翌朝へちょうど1日進行を確認した。ユーザーの保存後のScene差分は天気2値のみで、日記UI/入力接続は不変。最終3runの主要13CSとSceneのhashが現行と一致することを独立監査した。自動結果はPassedだが、描画・IME等を含む全体statusはManualVerificationRequiredのまま保持する。

|最終修正版のID|run ID|自動チェック|保存/Scene保全|
|---|---|---|---|
|diary.night.normal|d30e711c6d1042e9bcc325562fd78de2|15/15|成功|
|diary.night.special|4b859a1247ed4272ab34c7ff27dc433a|15/15|成功|
|normal.table|377e8fc788e74412a29f20e896a55f20|13/13|成功|

## 修正と失敗記録

- Domain Reload後に通常保存へ戻らない隔離ラッチ、共通Prefs窓口、照明の一時profile、全Assets/global EditorPrefsの終了監査、report失敗時の停止を、既存ローカル実装から必要な差分だけ切り出した。
- 日記の時間進行内にある夜挨拶待ちへ、期限・連動取消と限定した送りを追加。最初の修正で`diary.night.normal`は60秒Timeoutになった。通常入力の実行中ガードがテスト自身も拒否していたため、通常の遮断を維持した隔離シナリオ専用の送りへ分離した。Timeout時も9,113項目の保全は成功し、元レポートを保持した。
- 一括実行の開始拒否後queue残留、失敗後継続、予約後取消の無視を修正した。実際のRequestBatchによる連続Playは未実行。今回の実機検証は一件ずつ行った。
- 運搬の初回はUnityウィンドウX位置のEditorPrefs 1項目だけが変化しTestFailedとなった。通常保存/Assets/Sceneは不変。キーの安定確認後、監査例外を増やさず同じIDを再試行し全件一致。最初の失敗も保持した。

## 検証範囲

現ソースから待機/queue/入力の実装を抽出した外部メモリー検証は36ケース成功。通常入力の遮断、Console所有/閉じた同フレーム、menu/delayed/choice、typing、期限/取消、late fault、旧不具合のnegative controlを含む。Unityの正式NUnitや実保存adapterの実行件数ではない。

PRは既定ブランチ`930b607317b1755aa3c9070855cec3301e6b9a7c`から必要な12CSだけを切り出した。現行checkoutには別のRuntime/Scene変更があるため、上記の実Play成功をPRだけの実行成功へ読み替えない。PR限定ソースで外部Runtime/製品Editor・Windows Dev/Release条件compileと公開メモリー検証36件が成功。fake Unity環境での保存隔離36観測と旧実装2漏れのnegative controlも確認した。Editor全体にはbase共通の未収録WrightAngle.Waypoint参照テスト8エラーがあり、旧61テストを除く製品Editor compileの成功を全体成功として扱わない。

実IME、描画・カメラ・音声、Windows/WebGL/モバイルPlayerは要手動確認。専用worker、旧NUnit、通常保存adapterの停止条件を維持する。CodexによるMainScene保存・値復元、会話本文/CSV/Prefabの修正、Issue Close、既定ブランチへのマージは行っていない。

再実行と拡張手順は[DebugScenarioSafety.md](DebugScenarioSafety.md)、ID/結果/対象source hashは[機械可読証跡](DebugScenarioVerification-20261029.evidence.json)。実行レポート本体と保存監査のsnapshotはローカル`Library/DebugConsole/Reports/`へ保持し、保存内容やレジストリー値を公開しない。
