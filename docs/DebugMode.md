# デバッグモード

Issue #60以降、時間帯送りと日数の前進は共通TimeFlowを通す。日数の前進も夜の境界で一旦停止し、日記・就寝後に翌朝へ進む。日付巻き戻し・日付リセット・10/1日記の検証用絶対日付設定はデータ検証用として直接設定を維持する。詳細は`docs/TimeFlow.md`を参照。

`IntroPopupPanel` の `DebugButton` から開始すると、保存済みの名前設定をそのまま使用して命名画面を飛ばす。OP の Timeline は再生しないが、OP完了用に設定した `CameraEventPivot` の最終姿勢を適用してから、既存のOP完了処理を通じて通常猫・視線・会話 UI を復帰する。古いタイトル Scene に `DebugButton` が未保存の場合は、既存の開始ボタンと同じスタイルで実行時に補う。

この開始状態は保存日の Morning（保存なしではDay 1）であり、天気の初期化は `TimeManager` の通常処理を利用する。Debug Start 中だけ `TimeOfDayWidget` の左に `Debug` ボタンを表示し、日数、時間帯、現在天気、翌日予報を既存 `TimeManager` API 経由で変更できる。加えて6種類の心理値（Affection、Sadistic、Concern、Hostility、Obedience、Instinct）を `StatusManager` 経由で±10ずつ確認・変更でき、上位2値を使った代表心理Stateも表示する。AffectionまたはObedienceが70以上で、AudioClip設定済みかつプレイヤーが可聴距離内ならゴロゴロ音の条件も再評価される。デバッグ専用の GameState、時間帯、天気、心理値データは持たない。


通常のリリースPlayerではDebug Startボタンを非表示にし、自動生成と開始操作も禁止する。EditorとDevelopment Buildでは従来どおり使用できる。PC版ビルドメニューはDevelopment Build等をOFFにし、ビルドSceneコピーからデバッグUI・既知のマーカー/詳細ログ設定を無効化する。編集Sceneの設定は維持する。詳細は [PC版リリースビルド](PcReleaseBuild.md)。


## 共通開発Console（Issue #74〜#80、段階実装中）

Editor/Development Buildのみ、MainSceneのゲーム開始後にF1で開閉する。通常StartとDebug Startを区別せず開ける。入口判定は現行MainSceneに存在するNekolposGameBootstrap.HasEnteredGameplayを使い、GameManager不在を理由にManagerを追加しない。タイトル・Releaseでは開けない。IME変換中は開閉を保留する。WebGL/OSのF1競合は実ブラウザで要確認、InspectorのToggle Keyから変更可能。

見た目の正本はAssets/Resources/Debug/NekolposDebugConsole.prefab。RuntimeはこのPrefabをSceneごとに1個Instantiateし、Scene寿命で破棄する。NekolposDebugPanelの既存操作をPrefabへ移し、時間・心理値の正本は既存Managerを維持する。Debug StartのDebugボタンは共通Consoleへの互換入口。Editor生成はNekolpos > Debug > Create Missing Console PrefabとUpgrade各項目（既存要素を再生成しない）。Sceneを自動保存しない。

ConsoleはTimeScaleとPauseを変更しない。表示中はゲーム入力の共通ゲートと他CanvasのGraphicRaycasterを遮断し、フォーカス/カーソル/Navigationを保存する。閉じたフレームまで誤入力を抑止し、元の入力フォーカスとカーソルを復帰する。進行中イベント自体は停止しない。Esc/F10/散歩/会話入力もゲートを参照する。

「現在状態」は日時/天気/エリア/拠点/心理値/制御・UI状態を正本から読む。接続不在・未初期化は未接続とし、取得目的で初期化・Manager生成をしない。「状態・場所」の操作は通常会話入力待機だけ許可し、Pause、Tablet、散歩、学習、日記、TimeFlow、死亡復帰、Timeline、暗転、シーン退出中に理由付きで拒否する。Table/Deskは既存MoveToTable/MoveToDeskの運搬Bindingを通る。ConversationDebugPanelは現行MainSceneに未配置であり、接続不在を表示する。デバッグ目的のStatusManager追加は廃止した。

「診断」は既定500件（16〜2000）のリングバッファ。入力受付は長さだけマスク記録し、会話選択はLabelHash/行数、心理値はbefore/after、時間要求・時間更新、日記開始/完了、入力復帰を相関IDと時刻で記録する。発話本文、名前、認証情報、通常保存先はJSONに含めない。種別・相関IDで絞り込み、状態JSON/ログのコピー、ローカル書出しを提供する。WebGLはコピーのみ。通常配布版では記録呼出し自体を条件コンパイルで除外する。全会話候補の除外理由・任意Timeline内Signal・描画演出は未計測。

## テストセッションの隔離境界（Issue #77）

EditorではPlayを停止しNekolpos > Debug > Start Isolated Playから新規Playを開始する。Development Standaloneは起動引数--nekolpos-isolated-debugを使用する。隔離をScene Awake前から適用するため、既に進行中の通常プレイをその場で隔離へ切り替えない。F1シナリオは隔離未保証なら拒否する。Releaseには起動引数の処理も含めない。

進行保存は既存ProgressSaveStoreで空のメモリ領域へ分岐する。通常PlayerPrefs/クラウド用progress-v1.jsonの読出し・移行・書込・消去・Saveを実行しない。通常名前・心理値・日付・会話Sequence・発見数・学習語・日記草稿/Pending/カレンダー・死亡記録はすべてこの正本を通る。セッションをFinishしても隔離は解除せず、OnDisable/OnQuit/タイトル復帰後もテスト領域を維持する。通常プレイ再開はPlay停止後の新規Play（Standaloneは引数なしで再起動）。異常終了でも進行はメモリのみなので通常保存に書き戻さない。

猫のCatDataSOは元AssetごとにRuntime複製を共用し、Bootstrap/DialogueManagerからの名前変更が元Assetへ漏れないようにする。DiaryJournalStoreのStatic草稿とLearnedWordStoreのStaticは新規Playでリセットする。

進行以外の監査: GameSettings保存・OPスキップ設定保存・Runtime Source選択保存は隔離中抑止（既存デバイス設定の読出しは許可）。DialogueLogManagerはInstall IDを一時IDにしJSONL保存とアップロード/削除/レビュー書込を拒否。LogExporter/UploadHistory/UploadServiceの保存先もtemporaryCachePath/NekolposDebug/<session>/ConversationLogsへ分離する。PerformanceDiagnosticsは隔離起動しない。写真Preset保存/撮影とPrintScreen保存も隔離中拒否する。明示した診断エクスポートとEditorレポートのみ別の診断出力へ保存する。任意外部Pluginや未監査コード・MainPhotoSceneはシナリオ許可対象にしない。

シナリオは安定IDの許可リストを使い、ID・名前・必要モード・Fixture・接続条件・拒否理由を一覧表示する。未登録の文字列をコマンドやコードとして実行しない。


## 場面ジャンプと複合イベント（Issue #78/#79）

DebugScenarioExecutor.RunAsync(id)はF1とEditor Runnerの共通入口。許可済みID、隔離開始済み、通常会話待機、安全ガードをすべて確認してから副作用を開始する。同時要求は拒否する。before/afterとCorrelation IDを返し、実行中はゲーム入力だけ遮断する。成功、拒否、キャンセル、失敗、要手動確認を別ステータスにする。待機上限は通常30秒、死亡60秒（実時間）。Scene退出はキャンセルとして扱う。失敗/キャンセル時は隔離を維持したまま既存ResetOpenBetaStateとMainScene再読込で安全なタイトルへ戻す。途中の通常イベントを強制中断する用途には使用しない。

- normal.table / normal.desk: MoveToTable / MoveToDeskの正規運搬と完了条件で通常入力へ。
- walk: 正式なTryEnterWalkModeでPlayer/Collider/Camera/Areaと歩行入力を切替。
- tablet.desk: 正規Desk運搬の後、EnterTabletCameraViewと表示電源/Camera遷移の完了を確認。
- transition.table-desk: Table初期化後にMoveToDeskを通す。運搬Preset/2D素材のValidatePresetが成功しなければ非対応。
- diary.night.normal / diary.night.special: PRD_CONVERSATION_RESULT_NORMALの既存DiaryVariantsで朝/昼/夕の草稿を準備。specialはTEMP_SPECIAL_DIARY_001の既存特別本文をPendingへ1件追加する。Fixture設定に限り正規SetCurrentTimeで夕へ置き、夜への進行はRequestTimeAdvanceAsync/TimeFlowを使用する。F1実行は夜記入UIへの到達をManualVerificationRequiredとして返し、通常のボタンで記入・カレンダー確定・電源終了・就寝を行う。新規本文は生成しない。
- death.respawn: 既存DialoguePreview.csvのdeath:pressに対応するTriggerPlayerDefeat("press", timedEventKey: "action_paw_press_death_result")を使用する。任意Animation/PostPatternは指定せず、既存死亡記録/Pending/黒保持/朝日/翌朝配置/通常入力を検証する。日付がちょうど+1、Morning以外なら失敗。

ReturnToInitialAsyncは散歩終了・Tablet電源終了・Table運搬の正式経路で復帰する。日記記入・確定中、就寝中、TimeFlow、死亡、運搬、Pause、Camera遷移中は復帰を拒否する。入力フォーカスの最終復帰と演出の見た目は実Playerでも確認する。

## Codexからの固定操作（Issue #80）

既に起動した同一プロジェクトのUnity MCP execute_codeで、以下を明示的に呼ぶ。別Unityバッチプロセスや独自通信口は起動しない。MainSceneを開いたPlay停止状態からのみ開始でき、未保存Sceneを自動保存しない。

```csharp
return Nekolpos.EditorTools.DebugScenarioEditorRunner.Request("normal.table");
```

返されたLibrary/DebugConsole/Reports/<runId>.jsonをファイル読み取りで確認する。phaseがFinishedになるまで完了扱いしない。MCPの再接続が必要になってもファイルは残る。EditorメニューNekolpos > Debug > Run Scenario normal.tableも同じ入口。

明示した許可済み複数IDを、それぞれ新規隔離Playで一括試験する:

```csharp
Nekolpos.EditorTools.DebugScenarioEditorRunner.RequestBatch(new[] {
    "normal.table", "normal.desk", "walk", "tablet.desk",
    "diary.night.normal", "diary.night.special", "transition.table-desk", "death.respawn"
});
return "requested";
```

Runnerは既存開始メッセージを通常の進行処理で送り、共通Executorを実行する。日記は実際の朝/昼/夕・確定ButtonのonClickを使い、本文の完成を待って確認し、カレンダー保存/Pending消費/電源終了/就寝による翌朝+1まで検証する。各シナリオ後に通常Table入力へ戻り、Console1個/TimeScale維持/Errorなしを確認してPlayを終了する。終了後まで通常保存・PlayerPrefs・元CatDataSOのハッシュと未保存Scene状態を比較する。保全検証が失敗した場合はバッチの続行を止める。Unity自身の3つのplayer_session/connect.session値はPlayごとに変更されるため、通常ゲーム保存の比較から除く。

レポートはUnity版/Build target/ID/実行環境/経過時間/失敗理由/検証項目/before-after/後始末後snapshot/Trace/Correlation ID/保存・Scene保全/再現手順を持つ。生のErrorログは発話や保存先を含み得るためマスクし、構造化Traceと失敗チェックで原因を示す。automatedStatus=Passedは自動状態検証の成功、status=ManualVerificationRequiredとmanualChecksは描画・日本語IME・実Playerの未保証範囲。TestFailed、EnvironmentUnavailable、Cancelled、Rejectedを成功に置換しない。NUnit XMLがLibrary/DebugConsole/Regression.xmlにある場合は関連付けるが、シナリオ呼出し自体によるNUnit実行とは記載しない。

新規登録はDebugScenarioCatalogの型付きKind/安定ID、接続・Fixture検査、共通Executorの正式API、結果条件、回帰検証を同時に追加する。通常保存への直書き、任意文字列の実行、任意Transform/UI強制変更を追加しない。未対応素材はPrerequisiteReasonへ理由を返す。完了Issue報告にはID・自動結果・XML・要手動確認を明記する。レポートや通常保存を自動外部送信しない。


## 保存・終了の補足

入力所有権はDebugConsoleInput.LeaseをConsoleと既存会話デバッグで共有する。表示用GameObjectが破棄・非Activeになった場合でもLeaseを解放し、タイトル復帰は既存ResetOpenBetaStateの先頭でConsole/Leaseを閉じてから通常のCursor/Pause/TimeScaleリセットを行う。復帰元UIが既に非Active/破棄済みなら再選択しない。UnityのIMEとWebGL外部IMEの変換中は開閉を拒否し、変換本文を確定・破棄しない。

隔離の解除はEditorのPlay終了後だけ行う。Finish直後・Scene退出中・OnDisable/OnQuit中には通常Storeへ戻さない。シナリオ失敗時はセッションをFinishし、安全なタイトルから新規隔離Playを要求する。

初回の既存統合テストは通常保存へ日付・草稿・会話ログを残したため、統合テストを隔離Playへ改修した。作業前に記録したSHA-256と一致する元の日付/草稿/会話ログへ復元し、追加PerfDiagファイルをLibrary/DebugConsole/PreIsolationPerfArtifactsへ退避した。通常ゲームデータ・猫Assetの一致を再確認した。Unity自身のセッション値、NUnit TestResults.xml、Unity PerformanceTestingのPT_Runはテスト成果物として区別する。通常の学習辞書に検出した既存Invalid answerは内容を変更していない。

### 実装の境界

| ファイル | 責務 |
| --- | --- |
| NekolposDebugConsole / DebugConsoleInput / Console Prefab | 開閉・共有入力Lease・既存状態の表示、見た目の正本 |
| NekolposDebugPanel / DebugConsoleGuard | 既存操作の接続、通常待機以外での拒否 |
| DebugConsoleTrace / DebugConsoleSnapshot | 上限付き診断・確定状態の読み取り、ID以外の本文を記録しない |
| DebugSaveSession / ProgressSaveStore | Scene Awake前から終了までの空メモリ保存領域、元CatDataSO複製 |
| DebugScenarioCatalog / Fixtures / Executor | 許可済みID・既存CSV Fixture・正規状態遷移・タイムアウトと復帰 |
| Editor DebugSessionMenu / DebugScenarioEditorRunner | 明示したIDの新規隔離Play、終了監査、Library内レポート |
| Editor DebugConsoleAuthoring / DebugConsoleReleaseProbe | Prefabの初回/明示移行、Release/Development条件コンパイルとメタデータ監査 |

ReleaseProbeはゲームDLLを実行ロードせずUnity.Cecilでメタデータを読む。Windows/WebGLとも通常配布条件ではConsole入口・シナリオ・旧会話デバッグの変更APIが存在しない。通常のゲーム用状態APIは維持する。CompilerのInspector注入フィールド警告等はPlayer Build結果と区別して記録する。


## 2026-10-14の検証結果

正規EditMode回帰232/232成功（実Play遷移を含む）、追加の入力共有/破棄/既存Pause回帰30/30成功、失敗0/Skip0。初回の失敗は保存に依存した旧統合テスト、旧UnknownWord時間進行への期待、非Activeの旧入力欄への再選択期待を修正して再検証した。

新規隔離Playで8シナリオを一括実行し、すべてautomatedStatus=Passed。各モードでConsole開閉、外部IME変換フラグによる拒否、入力所有、Cursor/Pause/TimeScale、妥当なUI選択の復帰、正規経路によるTable入力復帰、Errorなし、通常保存/Prefs/猫Asset/Scene保全を確認した。夜2種類は既存Buttonから記入・確定・カレンダー保存・Pending消費・電源終了・就寝翌朝+1まで成功。運搬Preset接続と死亡翌朝+1も成功。Consoleを開いたままタイトル復帰/Scene再読込で1インスタンス、入力解放、カーソル解放、TimeScale1、タイトルでの開閉拒否と通常保存保全を別途確認した。

Windows Release Playerの実Buildと配布Assembly-CSharp.dllのメタデータ監査を実施。WebGLは283 RuntimeソースのRelease条件コンパイルでエラー0、変更API不在を確認（実Player Build/ブラウザは未実施）。Windows Development条件もエラー0で必要なDebug APIを保持する。Standalone/WebGL条件コンパイルのCS0649等のInspector注入フィールド警告は実Player Buildとは別集計する。旧WordLearning完了コールバックのCS4014は既存のfire-and-forget方式を明示し、例外通知経路を保持した。

実キーF1/Esc/F10、実日本語IME、Windows/WebGL/iOS/Android実Playerの操作、見た目/カメラ/視線/朝日/運搬はmanualChecksを人が確認する。MainSceneに未配置の既存ConversationDebugPanelは非接続表示とし、配置済み環境への導線を提供する。通常保存の既存学習辞書Invalid answerは未変更であり、別途データ整合確認が必要。

証跡: output/debug-console/regression-final.xml、lifecycle-final.xml、Library/DebugConsole/batch-summary.json、Reports/、normal-full-recovery.json、title-return-result.json、ReleaseProbe/、windows-release-verification.json。Build出力はGit対象外Build/DebugConsoleRelease。
