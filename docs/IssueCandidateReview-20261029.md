# Codex作業候補の確認（2026-10-29）

対象を **#93 → #80 → #32 → #81** の順に評価した。今回は候補調査と報告PRの作成まで。新たな機能実装、Unity操作、Play、Runner、Build、保存adapterの実行は行っていない。既存Issueの完了・受入合格を宣言する報告ではない。

## 判断

|順序|Issue／現在のラベル|現状|次の候補作業と開始条件|
|---|---|---|---|
|1|[#93](https://github.com/kissho222/Nekolpos/issues/93)／Open・codex-review|静的チェックMVPはローカル実装済み。最新レポートは環境不足|依存差分を分離レビューし、編集停止・非Play・compile完了を確認して静的チェックのみ再検証。新規Validatorは不要|
|2|[#80](https://github.com/kissho222/Nekolpos/issues/80)／Open・codex-review|Runner実装済み。最新normal.tableは終了保全がTestFailed|ユーザーによる並行Assets編集終了確認と既存限定承認の条件充足後、normal.tableだけ再試験。他7件・一括・正式NUnit Test Runnerへ拡張しない|
|3|[#32](https://github.com/kissho222/Nekolpos/issues/32)／Open・ラベルなし|旧監査・旧Windows Build実績あり。現行保存／追加機能に差分|監査更新と対象snapshot固定を先行。保存隔離・実行許可を確認するまでBuild／Player／実保存試験は保留|
|4|[#81](https://github.com/kissho222/Nekolpos/issues/81)／Open・codex-working|移動音・猫声基盤あり。最新9テスト、通常四足歩行接続、聴感は未検証|仕様と現物の同期・Inspector不足の独立差分化が候補。未確定の四足接地へ接続しない|

## 調査対象の版と承認範囲

- ローカルは `chore/ai-handoff-foundation`、HEAD `ea13005bed6941ce6c5393c75bab9ab022c51039`。調査開始時の `git status --short` は415項目。未追跡ディレクトリは1項目に畳まれるため、変更ファイル総数ではない。
- originの既定ブランチは `chore/ai-handoff-foundation`。取得した先端は `930b607317b1755aa3c9070855cec3301e6b9a7c`。`main`というremote refは存在せず、この既定ブランチをPRのbaseとする。直接コミット・マージはしない。
- ローカルHEADとremote先端の差はAGENTS.mdの起動許可規則。ラベルは実行許可ではなく、所有者の対象別直接指示を要することを確認した。今回の直接指示は4件の候補検討と結果PRであり、未検証依存のある実作業開始へ拡張しない。Openの `codex-task` Issueは検索時0件。
- `.ai/HANDOFF.md`、各仕様、現行コード／Sceneの読み取り、GitHub本文・コメント、既存検証ファイルを照合した。ラベルや古い「完了」記述だけでは判定していない。
- `docs/TestSaveIsolation.md` のworker／旧fixture制限と、後続 `docs/ProjectAudit-IsolationReview-20261025.md:94,124` のnormal.table限定承認・並行編集終了待ちを併せて適用する。候補検討の依頼を、並行編集終了の確認や全テスト再開の承認とは解釈しない。
- 本PRは調査文書・証跡manifest・引継ぎの更新だけを含む。ローカルで観測した未コミット実装は同梱しない。manifestのSHA-256はそのローカル版を識別するもので、PRのコードが動作検証済みであるという意味ではない。

## 1. #93 制作データの静的品質チェック

### 既存実装と依存

`Assets/Scripts/Editor/ProductionDataChecks.cs:40,55,297` にMenu／RunBatch／Run／VerifyFixturesがある。既存Importer、Validator、CSV parser、日記公開検証、読み辞書正規化を再利用し、CSV構造・必要列・キー・時間値・日記参照・辞書・候補JSONを検査する。出力はLibrary内のJSON／CSV／TXT、終了コードは0／1／2。原稿を自動修正しない。

`ProductionDataChecks.cs`・meta・`docs/ProductionDataChecks.md` はローカル未追跡で、PR baseにも存在しない。さらに静的条件検査の `ConversationGameStateConditionEvaluator.ValidateSyntax` はPR baseにないローカル追加APIであり、`DialogueAuthoringValidation.cs` もEvaluateからこのAPIへ変更されている。チェッカーだけを取り出すPRでは依存が欠落する。該当API・Validator差分を先にレビューするか、必要な安全化差分だけを明示して同じ独立PRに含め、対象版で検証する必要がある。

`DialogueLegacyImporter` は改行差を除けばPR baseと一致する。単なるCRLF差を機能差として数えない。`DialogueDiaryCatalog` には別作業のspecialOnly変更があり、そのRuntime変更まで静的チェックの必須依存として無条件に同梱しない。

### 検証の世代

- 10/25の導入時記録：26fixture、Run／Menuの診断一致、Error 0／Warning 29／Info 1103、入力等103ファイル不変。`verification.json` と [完了コメント](https://github.com/kissho222/Nekolpos/issues/93#issuecomment-6388566813) で確認した。正式Runner・batch CLI・Buildではない。
- **現存する最新 `Library/ProductionDataChecks/report.json` は2026-10-28 08:29:46 JST（generatedUtc `2026-10-27T23:29:46.6642745Z`）、environment_unavailable／exitCode 2／Error 1／入力0件。** 診断codeはEnvironment。非Play、compile完了、入力ルート等をまとめた入口判定なので、どの条件が原因だったかはこのファイルだけでは特定できない。
- 最新reportには入力manifestがないため、10/25の入力と現在の原稿が同一だとは確認できない。古いverification.jsonを最新成功の代用にしない。今回再実行なし。

### 競合と次の範囲

#80は全Assetsの前後不変を監査する。実際にnormal.tableの2回目は本チェッカーの追加を検知して失敗している。Assets編集・import／compileと#80の試験は直列化する。

Openのdraft [PR #84](https://github.com/kissho222/Nekolpos/pull/84) は心理会話CSV・SystemTimedEvent・DialogueIdentityを変更し、#93の入力と重なる。作者による原稿判断と#84の採否を静的エラー修正に混ぜない。チェッカーの実装分離、依存確認、安定した入力snapshotの静的再検証を次の最小単位とする。

## 2. #80 自動シナリオテスト

`DebugScenarioEditorRunner.cs:50` と共通Catalog／Executorには8許可IDが実装済み。独自Runnerの再新設は不要。依存#74〜#79はすべてOpen／codex-review。Issueコメントの232＋30テスト・8シナリオ成功は10/14時点の履歴である。

10/25にはDebugSaveSession、WorkerPlayerPrefs、DebugNormalSaveAudit、DebugScenarioEditorRunnerの4ファイルを補強。現行SHAは独立レビュー記録と一致する。しかし限定承認されたnormal.tableの2回とも、シナリオ状態はSucceeded、終了保全はTestFailedだった。

最新 `Library/DebugConsole/Reports/faa7a1f2f26040349a1607ff44248401.json` はFinished／TestFailed、normalSavesPreserved=false、scenePreserved=true。保全snapshotの唯一の差分はProductionDataChecks.csの追加であり、通常保存の破損が確認されたという意味ではない。この後の合格レポートは確認できなかった。

再開条件は `docs/ProjectAudit-IsolationReview-20261025.md:124` の「次のPlayはユーザーが並行Assets編集完了を確認するまで保留」。確認後も、import／compile終了・安全コードの実assembly反映・新baseline取得を満たしてnormal.tableだけを行う。終了保全合格前にnormal.desk／夜日記／batchへ進めない。worker起動、旧67fixture、正式NUnit、実保存adapterはこの限定承認に含まれない。

MainScene、Bootstrap、DialogueManager、DiaryJournalStore、TimeFlow、ProgressSaveStoreの既存変更と並行するため、過去の8件成功を現行統合の証拠へ流用しない。

## 3. #32 Windows版互換性監査

10/03の `docs/WindowsStandaloneAudit-20261003.md` と [実Player追補](https://github.com/kissho222/Nekolpos/issues/32#issuecomment-5966407331) には、Windows／WebGL条件compile、Windows Development Build（Error 0／Warning 39）、別プロセスでの保存・再起動の確認がある。batchmode／nographicsであり、IME・描画・音声の合格証拠ではない。10/25のRelease容量Build成功（Error／Warning 0）もPlayer起動はしていない。

現行版との差を次回監査へ反映する必要がある。

- `ProgressSaveStore.cs:64,99` と `docs/SteamCloudSave.md:13`：10/22にcheckpoint方式へ変更。nativeはpersistentDataPath下のJSON、Editor／WebGLはPlayerPrefs。復帰期待値は「保存日の朝」から「最後に確定した時間経過の到達時刻」へ変わった。現在の記録は外部ダミー検証で、実保存adapter／実プロセス再起動は未実施。
- `NyanstagramTransport.cs:56`、`PettingModeController.cs:244`：新しいWebGL bridgeは条件コンパイル内、Windows通信はUnityWebRequest経路。既存WebGLImeInputBridgeも条件分岐を維持している。これはソース確認であり、新規Build合格ではない。
- `OpenBetaPauseMenuController.cs:562,634`：Application.OpenURL／Quitの経路が存在する。Windows profileは `m_OverrideGlobalSceneList: 0` でglobal Scene listを継承し、MainSceneが有効。profile内の空m_Scenesを欠落扱いしない。

保存・Pause・ChatUI・MainScene・Bootstrapは他作業と共有し、Nyanstagram／Pettingには未追跡実装がある。対象snapshotと保存隔離条件が未確定のままBuildやPlayer保存試験を開始しない。まずこの差分一覧と手順を固定し、旧版の実績と現行未確認を分ける。

手動確認は日本語IMEの確定／送信Enter・候補位置・Esc、Alt+Tab／フォーカス、DPI／複数画面／全画面、音声、外部リンク、現行checkpoint再開、アセット収録。16:9固定、runInBackground、Escの優先、WebGLセーブ移行はユーザー判断事項として維持する。

## 4. #81 SE基盤

依存#38・#61はOpen／codex-reviewedで、それぞれ68＋1件、19件のテスト成功報告がある。現在のMixerにはMaster／BGM／SE／CatVoiceと4公開パラメーターがあり、移動音と猫声は既存SettingsManager.RouteAudioを使用する。依存の実装を重複させる必要はないが、Windows実聴感・端末操作は未確認。

`CharacterMovementAudioController.cs:67`、`PlayerSafetyMovementController.cs:144`、`CatVoiceAudioController.cs:68` に床材の明示無音、プレイヤー接地・ジャンプ通知、猫声4カテゴリ、Pause対応がある。Timeline Signalの入口もある。

現物は初期仕様の「音源なし」より進んでいる。MainSceneではジャンプ音は空、着地音は既存footstep_wood_000.ogg、嬉しい／不満は各1音、呼ぶ／怒りは空。DeskSurfaceUnconfirmedは5音、Table側は空、旧猫足音は2音ある。今回音源の採用・変更はしていない。

猫足音のcontactObserverはMainSceneでnull。SampleAfterPose呼出はSinglePawContactSession内のみで、通常四足歩行の最終姿勢通知は未接続。足ボーン校正・地面Layer・姿勢評価順序が未確定のため、推測で接続しない。三目並べ／ホッケーの足接地と競合する。

10/15の8/8成功とWindows Development Build成功は既存証跡で確認した。ただしcheckpoint.jsonとformal-tests-raw.jsonのjob IDは一致せず、同一runとは断定しない。10/19に接地回帰1件が加わった現在は9テストで、MovementAudioTests.csは `NEKOLPOS_TEST_WORKER && NEKOLPOS_TEST_REVIEWED_LEGACY` により通常Editorから除外される。**現行9件の正式実行は未確認**。古いHANDOFFの「8件未検証」を最新状態として転載することも避ける。

追加・変更対象の `CatFootstepAudioController.cs:16` には英語Header／編集フィールドが残り、contactObserver／defaultSurfaceにも日本語表示・Tooltipがない。対応CustomEditorは調査範囲で見つからず、Inspector全項目日本語化の受入不足候補として記録する。

最小の次作業は現在の割当・検証世代の仕様同期、Inspector不足の独立差分化、正式検証条件の確認。移動安全修正、足接地、MainSceneのユーザー調整、#38／#61のMixerを巻き込んだ変更や通常歩行への接続は先行しない。

## 並行作業とPRの境界

調査時、同じcheckoutで「Sequenceの日記回数を確認」がactiveだった。日記Sequence、心理会話、OP、ゲームセンター、Timeline復帰の新しい作業記録もあり、日記／会話／保存／時間進行／足接地は安定済みとは扱えない。他チャットへ追加指示・停止依頼は送っていない。

本PRの文書変更はPR #84の3データファイルと重ならない。引継ぎには今回の短い現在状態だけを追加し、ローカルに蓄積した別作業のHANDOFF全文やAssets差分を公開しない。既存作業を一括commitせず、既定ブランチを親とする独立した `codex/` ブランチへ文書だけを記録する。

## 今回の確認と未実施

GitHub本文・コメント・依存ラベル・Open PR、現行仕様／コード／Scene、既存レポート、baseとの差分を読み取り確認した。対象37ファイルのSHA-256とbase存在／差分は [証跡manifest](IssueCandidateReview-20261029.evidence.json) に記録した。診断本文・会話ログ・保存値・秘密情報は転記していない。

新規Unity実行0、テスト実行0、Build実行0。ファイル／リンク／manifestの静的整合性とPRの変更範囲のみを検査する。4Issueのラベル・本文・状態は変更せず、Closeやマージは行わない。
