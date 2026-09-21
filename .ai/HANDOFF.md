# 開発引き継ぎ

> 現在状態のみを記録する。秘密情報、APIキー、個人情報、会話・Issueの全文は記載しない。

## 更新日時

2026-09-21 JST

## 現在の作業

基本会話地点のCondition `H`、会話CSVから通常Timelineを起動する場所移動演出、タブレット閲覧カメラの遷移、日記カレンダーと夜の記入フローを実装・設定済み。会話からの基本会話地点移動は、フェードの完全暗転イベントを同期点にして猫の配置・姿勢・視線を暗転中に完了し、明転後に必要なタブレットカメラ遷移を開始する。タブレットのNyansta・Shop・demae・Diaryは、アイコンから拡大しCloseButtonで縮小してStartPanelへ戻る共通遷移を利用する。DiaryPanel内はCalendar・Detail・Writingの共通左右スライドと、DetailBackImageだけを動かす日送りを使う。GitHub Issue #9のプレイヤー安全移動基盤は、本番用`PlayerSafetyMovementController`として写真撮影用`FreeCameraController`から分離して実装済みであり、通常歩行・壁衝突・床抜け防止・SafePosition復帰・端落下防止はユーザー実機確認済みである。Issue #10の現在地エリア判定基盤は`PlayerAreaTracker`と`PlayerAreaVolume`として実装済みである。現ファイルでは#9のPlayer一式が`TitleScene`にあり、`GameScene`にはシリアライズされていないため、対象Sceneを確定するまでSceneへの追加・移動はしない。

## 現在の状態

`PlayerSafetyMovementController` は通常プレイ専用の`CharacterController`移動として、Inspector設定可能な`Walkable Layers`と最大傾斜によるSphereCast接地判定、進行先の足場確認による端落下防止、安定接地後だけ保存する`LastSafePosition`、異常落下時の復帰を提供する。写真撮影用`FreeCameraController`とは分離した。身長5cmの初期値は高さ`0.05`、半径`0.01`、`center.y=0.025`、`stepOffset=0.01`であり、子カメラの目線高は`0.043〜0.045`を目安にする。`SetMovementInput`はモバイルUIスティックなどの入力注入用であり、従来のHorizontal/Vertical軸は入力Override未設定時だけ使う。`SetEdgeFallPreventionEnabled`、`SuspendSafetyRecovery`、`ResumeSafetyRecovery`、`TeleportTo`、`SetLastSafePosition`、`ResetLastSafePosition`をイベント・遷移側の公開APIとする。`Walkable` Layerは`TagManager.asset` index 7に定義済みである。#9のユーザー実機確認では、通常歩行、壁衝突、床抜け防止、LastSafePosition復帰、崖端停止が成功している。壁密着時にカメラ越しに壁の向こう側が見える現象は、Near Clip Plane最小値でも残る別課題として保留している。現在の`Player`一式は`TitleScene`にのみ保存され、GameSceneのScene YAMLには存在しない。

`PlayerAreaVolume` はInspector設定可能な`Area Id`、`Priority`、`Resolve Order`とTrigger Colliderを保持する。`PlayerAreaTracker` はCharacterController中心（なければTransform位置）を使い、毎フレームAreaVolume候補を再評価するため、CharacterControllerのTrigger通知が不安定でも現在地を保持できる。重複時はPriority、Resolve Order、Area Idの文字列順で決定的に選び、`CurrentAreaId`、`CurrentArea`、`IsInArea`、`RefreshAreaNow`、`AreaChanged`で外部連携する。未所属は`None`であり、同じArea Idの継続ではイベントを再発行しない。

基本会話地点は `CatPositionController` が一元管理し、`Table` / `Desk` / `Bathroom` / `Bed` を保持する。会話Condition `H` はこの状態を参照し、複数位置のORと `!` 否定指定を評価する。通常会話と選択肢は同一の評価経路を使用する。

`TitleScene` の `OpenBetaTitleBootstrap > Conversation Timeline Actions` には、出発・到着の通常 `PlayableDirector` を共用する `MoveToDesk` / `MoveToTable` を登録済み。`OpenBetaTitleBootstrap` は `FadeController.BecameOpaque` を待ってから状態・猫・拠点カメラを遷移先へ更新し、待機姿勢・カメラ優先視線Rigの更新を暗転中に完了する。共有Timelineの再生時間は暗転中に待たず、`BecameTransparent` の後に必要なタブレットカメラ補間を開始する。`homeLocationChangeTime` は既存Scene互換用で実行時に参照しない。各移動先ごとのTimeline、Signal Asset、Signal Receiver Reactionは不要である。

`00350` のChoice「はい」Actionは `play_conversation_timeline:MoveToDesk` に設定済みで、暗号化済みTalkDataにも同期済み。

空テキストの `Action` 行は会話ページとして表示せず即時実行する。Timeline Actionの成功時は会話本文と選択肢を消し、通常入力モードへ戻す。

場所移動Timelineの開始時は猫をホームアンカーへ再配置しない。暗転完了後に遷移先へ配置するため、暗転前のモデル消失を防ぐ。

GitHubのOpen `codex-task` Issue #1を今回の外部指示として確認した。`MoveToDesk` の到着演出完了後は、`PlayerCameraRoot/TabletCameraPoint` を参照して `CameraEventPivot` をタブレット閲覧姿勢へ0.4秒で補間移動する。開始前の姿勢・カーソル状態・登録済み通常視点操作コンポーネントの有効状態を保存し、`ExitTabletCameraView()` で復帰できる。現在のTitleSceneには停止対象のFPS視点操作コンポーネントがないため、登録配列は空である。

`OpenBetaCanvas/TabletButton` は `OpenBetaTitleBootstrap` に接続済みで、`Desk` かつ通常会話の入力待ちだけ表示する。押下時は会話入力UIを隠して既存タブレットカメラ遷移を開始し、復帰後に通常入力待ちを再開する。

`UIStyle.ApplyTree` のボタン・テキスト一括テーマ変換では、`OpenBetaCanvas/TabletButton`を名前で例外扱いにする。TabletButtonの画像、Selectable色・遷移、子テキスト色、UIStyleBorder、ShadowはSceneまたは専用処理の状態を保持し、ほかのボタンの一括変換は継続する。対応する`UiInteractionRegressionTests`を追加した。Unity MCPブリッジは接続拒否のため、新規接続による即時・短時間待機後・最終の3回を試したが復旧せず、今回のUnity再コンパイル・テスト・Console確認は未実行である。

タブレット本体の画面は通常消灯する。`TabletDisplay` の `Start Powered On` はオフであり、`OpenBetaTitleBootstrap` が `TabletCanvas` と発光用`EmissionPanel`を閲覧カメラ到着時だけ同時に有効化する。Canvasはレイアウトを左右反転させないY=180回転を維持し、DisplayAreaの表面かつカメラ側となるローカルZ=+0.054に置く。`EmissionPanel`のZ=+0.051より前面となるため、本体Mesh Rendererに隠れない。Mesh Rendererを無効化・マテリアル差し替えしない。`TabletDisplayController`も電源状態を管理し、消灯時にCanvasGroupを透明へ戻す。したがって`OpenBetaTitleBootstrap`の電源ONでは、Active化に加えてCanvasGroupを`alpha=1`・操作可能へ復帰する。`PowerIcon` は実行時にButtonとして接続され、タブレット画面だけを黒へフェードしてから、反比例型イージングでDesk側の通常カメラへ復帰する。閲覧終了・タイトル遷移中断時には即時消灯する。

Unity MCP（`Nekolpos@7523023d`）で外部ファイル編集後にEditorメモリ上へ残っていたTabletCanvasの旧Z=-0.054をMCP経由でZ=+0.054へ更新してTitleSceneを保存した。画面確認・Console確認・EditModeテストも同接続で実施済みであり、MCPブリッジを再接続して追加のライブ診断・テスト実行が可能な状態を確認した。

GitHubのOpen `codex-task` Issue #2を外部指示として実装した。`TimeManager` は新規または開始日未保存のセーブで、ゲーム1日目に相当するPCローカル日付を `PlayerPrefs` へ一度だけ保存する。以後のカレンダー日付はPC時刻でなく、保存済み開始日と `CurrentDay` から算出する。範囲外の日数は安全に丸める。

`DiaryCalendarController` は `OpenBetaTitleBootstrap` に実行時追加され、`DiaryIcon` を閲覧専用の入口として接続する。`DiaryPanel` に配置済みの `CalendarView` と `DetailView` を内容更新だけして使い、前月・次月・今日・閉じる操作、開始月〜現在ゲーム内月の移動制限、日付セル左上の35pt日付とその下の本文先頭12文字プレビュー、複数日記本文の一覧表示を提供する。`AddEntry(string)` は夜の記入フローから呼べる保存入口として用意済みである。

GitHubのOpen `codex-task` Issue #7を実装した。`DiaryCalendarController`は、Calendar→Detail、Calendar→Writingを右から入る横スライド、Detail/Writing→Calendarを逆方向の横スライドに統一した。遷移中は関連Viewの入力・Raycastを止め、中断時も位置・可視・操作状態を復帰する。日送りは`DetailBackImage`（背景とDetailBodyを含むPrefabインスタンス）だけを複製して入れ替え、固定ヘッダーと操作ボタンを動かさない。夜の日記はCalendarを表示してからWritingへ進む。Unity MCPで再コンパイル後Console Error 0件、`TabletDisplaySceneRegressionTests`はEditModeで18/18成功した。GitHub CLIはユーザーPATHへ導入済みで、`.codex/tools/github_issue_progress.py` は`GITHUB_TOKEN`/`GH_TOKEN`がない場合でも認証済みGitHub CLIのKeyringからトークンを内部取得する。Issue #7は完了報告の投稿後、Openかつ`codex-review`へ移行済みである。

`DetailBackImage`のCanvasGroup未設定エラーは、破棄済みの`CanvasGroup`参照をC#の`??`で通常null判定していたことが原因だった。`GetOrAddCanvasGroup`をUnityの`group == null`判定へ修正し、破棄済みコンポーネントを必ず置換する。Unity MCP再読込後のConsole Errorは0件で、対応するEditMode回帰テスト1/1成功を確認した。

Diaryの横スライドとDetailBackImageの日送り中にタブレット画面外へページが描画されないよう、`DiaryCalendarController`が`DiaryPanel`へ不足時だけ`RectMask2D`を追加する。DiaryPanel自体を遷移用表示領域とするため、既存Sceneの各View配置を変更せず、すべての移動ページを物理的なタブレット画面内にクリップする。Unity MCP再読込後、対応するEditMode回帰テスト1/1成功、Console Error 0件を確認した。

GitHub Issue #8「特別な日記ソート・肉球表示・日送り対応」を実装し、完了報告を投稿後にOpenの`codex-review`へ移行した。`DiaryCalendarController`は通常日記と特別日記の種別を保存し、特別日記がある日だけを既存42セルへ日付順・左から右に1日1セルで表示する特別ソートを追加した。通常表示でも特別日が分かるようセル右上に肉球を表示する。特別ソートではDetailViewに同日の特別日記だけを記録順で渡し、Before/Nextは特別日付リスト上の前後へ移動する。DetailViewからCalendarViewへ戻ってもソート状態は保持する。`DiaryJournalStore`は最初の特別日記Pending取得時に解禁フラグをPlayerPrefsへ保存し、肉球付きソートボタンを以後の起動でも表示する。肉球は`Assets/Picture/肉球マーク.png`を実行時ロード用の`Assets/Resources/Diary/肉球マーク.png`へ同梱した。`TEMP_SPECIAL_DIARY_001`をSystemTimedEventへ追加し、Inspectorコンテキストメニューから現在ゲーム内日付に追加して一連の表示を確認できる。Unity MCPで暗号化CSVの読込結果（特別種別・本文・ID）を確認済みである。

GitHubのOpen `codex-task` Issue #4を外部指示として実装した。`TabletApplicationWindowTransition` はホーム画面アイコンの位置・大きさから任意のアプリWindowを全画面へ補間し、中断時も不透明・通常スケールへ確定する共通コンポーネントである。DiaryIconの手動起動はこれを使ってCalendarViewを開く。`BeginNightDiary()` はアニメーションを経由せず、通常草稿ではDiaryPanel直下のPromptParentPanelを表示し、特別PendingだけWritingViewを開く。PromptParentPanelはWritingViewの子ではなくDiaryPanel直下であり、ReWritingButtonは本文・マイク表示・完了/書き直しボタンを初期化してから夜の選択状態へ戻る。

`TabletHomeApplicationController` は `OpenBetaTitleBootstrap` が実行時に追加するタブレットホーム専用Controllerである。既存のIconParentとPowerIconをStartPanelへまとめ、`NyanstaIcon → NyanstaPanel`、`ShopIcon → ShopPanel`、`demaeIcon → DemaePanel` を接続する。同名PanelがSceneに未配置の間は、既存のTabletCanvasアプリ親Panelの子として最小ページとCloseButtonを生成する。DiaryPanelも同じControllerへ登録され、日記のCloseButtonはDiaryPanelをDiaryIconへ縮小してStartPanelを表示する。Sceneを直接変更せず、先行する未保存Scene変更との競合を避けた。

GitHubのOpen `codex-task` Issue #5を外部指示として実装した。`.codex/tools/github_issue_progress.py` は`origin`のリポジトリに限定し、既存の無関係ラベルを残したまま、着手時に`codex-task → codex-working`、完了時に結果コメント投稿後`codex-working → codex-review`、ユーザー確認後にCloseを実行する。完了報告は変更ファイル・実装内容・ビルド/テスト結果・ユーザー確認事項の4見出しを必須とし、認証情報やAPI失敗詳細を出力しない。Issue読取側は「タスク5」の日本語指定も明示Issue番号として解釈する。GitHub CLIのブラウザ認証を完了し、Issue #5を作業結果コメント付きの`codex-review`へ移行した。

GitHubのOpen `codex-task` Issue #6を外部指示として実装した。`AGENTS.md`に、MCP失敗時は再初期化して同一操作を即時・短時間待機後・最終の計3回再試行すること、MCP必須操作が全試行で失敗した場合は未実行のまま完了扱いにせず中断・報告すること、安全で同等な代替を使う場合は未確認範囲を明記することを定義した。GitHub CLIのブラウザ認証を完了し、Issue #6を作業結果コメント付きの`codex-review`へ移行した。

保存済み`StartPanel`がタブレットアプリ共通親を兼ね、`NyanstaPanel` / `ShopPanel` / `DemaePanel`がその子かつ`SizeDelta=10×10`で残る状態を検出した。`TabletHomeApplicationController`は、既存StartPanelがIconParentの直接親である場合にその親をアプリ共通Rootとして解決し、StartPanel・各アプリPanel・DiaryPanelをRoot直下の全画面RectTransformへ正規化する。`TabletApplicationWindowTransition`は中断時に開始前の全画面位置・スケールを復元する。Diaryは内部CalendarView用の別遷移を生成せず、DiaryPanel自体を他アプリと同じ共通遷移で開閉する。保存済みのNyansta/Shop/DemaePanelにCanvasGroupがない場合も、遷移前に必ず追加してMissingComponentExceptionを防ぐ。

GitHubのOpen `codex-task` Issue #3を外部指示として実装した。`DiaryJournalStore` は通常草稿と特別Pendingを確定済み日記とは分離して保存する。時間経過イベントはCSVの `diary_id` / `diary_kind=special` を解釈し、死亡フローは翌朝へ送る前に特別Pendingを追加する。夜へ切り替わった時間経過イベントの完了後、Deskとタブレット閲覧へ遷移する。通常時は日記アプリではないプレイヤーの思考UIで朝・昼・夕方を選び、選択後にのみ `WritingView` を独立ウィンドウとして開く。音声入力風の文字送り後、終了で初めてカレンダーへ確定保存する。`ReWritingButton` は未確定本文を破棄して思考UIへ戻すため、草稿・Pendingを失わない。

会話Action専用の `ConversationMove_Start_Director` / `ConversationMove_End_Director` は Play On Awake をオフにする。これにより起動時にDesk移動Timelineと暗転が自動実行されず、既定の基本会話地点 `Table` を維持する。

`C:\Users\shoho\20260814 Nekolpos.zip` 内の `Nekolpos/Assets/Resources/Dialogue/SystemTimedEvent.csv`（3,017 bytes、アーカイブ内更新日時 2026-05-25 02:37:30 JST）を復元候補として確認した。現行 `TalkSource/TalkCSV/SystemTimedEvent.csv`（1,741 bytes、2026-09-15更新）とは別系統の10キーであり、現行12キーを失わないよう上書きではなくマージが必要である。

`Build/PC`（2026-08-09）の `sharedassets0.assets` から、同梱鍵で復号した `SystemTimedEvent`（5,699 bytes、25キー）を確認した。現行12キーとの重複はなく、8月ビルド版と現行をマージすれば37キーを安全に保持できる。`Build_Release_Diag`（2026-07-23）にも21キー・4,949 bytes版があるが、8月版のほうが4キー多い。

`TalkSource/TalkCSV/SystemTimedEvent.csv` は8月ビルド版25キーと現行12キーをマージして37キーへ復旧し、`Assets/Resources/TalkData/SystemTimedEvent.bytes` も同じ鍵で再暗号化済みである。復号ラウンドトリップにより、実行用bytesとマージ済みCSVの完全一致を確認した。

## 完了したこと

- 写真撮影用`FreeCameraController`と分離して、本番用`PlayerSafetyMovementController`にCharacterControllerベースの通常歩行、Walkable判定、崖端防止、SafePosition復帰を実装した。
- プレイヤー安全移動の回帰テストと対象SceneへのInspector設定・公開APIを`docs/PlayerSafetyMovement.md`へ追加した。
- `PlayerAreaTracker`と`PlayerAreaVolume`を追加し、現在地の参照、重複領域の決定的な優先順位、未所属、変更イベント、Gizmoを実装した。
- エリア判定の設定・公開APIを`docs/PlayerAreaTracking.md`へ追加した。

- `H:` による現在位置Conditionと `!Desk` などの否定指定を会話・選択肢に対応させた。
- Dialogue PreviewのCondition UIで `H` と既存A〜Dの不成立フラグを編集できるようにした。
- 位置Conditionなしの既存CSVは全地点で有効なままにした。未知位置IDはWarningと不成立扱いにする。
- `OpenBetaTitleBootstrap` に `play_conversation_timeline:<ID>` の実行機構を追加した。
- `MoveToStart.playable` から旧方式の位置更新・終了連鎖Markerを除去し、FadeToBlackのみを残した。`MoveEnd.playable` のFadeFromBlackは維持した。
- `MoveHomeToDesk` / `MoveHomeToTable` / 旧終了連鎖のSignal AssetとReceiver登録を削除した。
- `DialoguePreview_Integrated.bytes` を現行CSVから再暗号化し、復号してAction文字列を確認した。
- 空テキストActionの即時実行と、Timeline起動成功時の会話UI終了処理を追加した。
- Timeline開始直前のホームアンカーへの即時スナップを削除した。
- `TabletCameraPoint` と会話Timeline完了後のタブレット閲覧カメラ遷移を追加した。
- `TabletButton` のDesk・通常入力待ち限定表示と、押下時のタブレット閲覧遷移を追加した。
- タブレット本体の `TabletCanvas` を常時Activeにせず、閲覧カメラ到着時だけ点灯し、閲覧終了時に消灯するよう変更した。
- 会話移動用のStart/End DirectorのPlay On Awakeを無効化し、起動時のDesk移動・暗転を停止した。
- `FadeController` に完全暗転・完全明転イベントを追加し、会話拠点移動を秒数ではなくイベントで同期するよう変更した。
- 会話拠点移動中はTimelineの既存フェードSignalを二重実行せず、暗転中にアンカー、姿勢、視線Rigを確定してから明転し、タブレットカメラは明転後に開始するよう変更した。
- 共有Timelineの再生時間を暗転中に待たないことで、基本会話地点移動の暗転をフェードと状態反映だけに短縮した。
- `TabletCanvas` は既存のY=180回転を維持したまま、DisplayAreaのカメラ側であるローカルZ=+0.054へ配置した。`EmissionPanel`のZ=+0.051より前面へ置くことで、Mesh Rendererを常時有効のまま表示できるようにした。PowerIconでタブレット内だけを黒フェードしてDesk通常カメラへ戻る処理を追加した。
- タブレット電源状態は`TabletCanvas`と`EmissionPanel`のActiveを同時に切り替えて表現するよう統一した。
- `TabletDisplaySceneRegressionTests` を追加し、TabletCanvasが表面側かつEmissionPanel前面にあり、Y=180の表示向きを維持すること、電源ONで透明だったCanvasGroupを不透明へ戻すことを検証するようにした。
- 基本会話地点の暗転・明転を各0.5秒に設定し、合計最低1秒の移動演出にした。
- 日記カレンダーとゲーム内日付基盤を実装した。`DiaryIcon` は通常の閲覧だけを開き、日記の追加は `DiaryCalendarController.AddEntry(string)` を明示的な夜の確定フローから呼ぶ構造にした。
- `DiaryPanel` にEditor編集用の `CalendarView` / `DetailView`、曜日、42日付セル、操作ボタンを配置し、日記を開いてもこれらを破棄しない構造へ変更した。
- `WritingView` をDiaryPanelのEditorプレビューとして有効化した。Play時は `DiaryCalendarController` が表示Viewを切り替える。
- Diaryの一覧・詳細・記入ViewをCanvasGroup付きの独立ウィンドウとして扱い、フェード／拡大で切り替える。`DiaryCalendarController` は `OpenBetaTitleBootstrap` にSceneコンポーネントとして付与済みで、遷移時間・開始スケールをInspectorから変更できる。
- `PromptParentPanel` を `WritingView` の子から `DiaryPanel` 直下へ移し、日記アプリのウィンドウとは別の思考UIとして分離した。
- タブレット内の旧 `UnityEngine.UI.Text` 66件をすべて `TextMeshProUGUI` へ置換した。旧 `ZenMaruGothic-Regular SDF` の多ページSDFアトラスはグリフ矩形が表示される不具合を起こしたため、全タブレットTMPテキストをZenMaruGothic由来のDynamic Bitmapフォント `Assets/Resources/Fonts/TabletZenMaruGothicDynamicBitmap.asset` へ統一した。`TextMeshPro/Mobile/Bitmap`を使うため追加SDF Atlas SubMeshを生成せず、日本語・中国語フォールバックも登録済みである。
- `WritingView` / `DetailView` にアプリ画面用の紙面背景・影を追加し、重なっていた終了／書き直しボタンを横並びの主・副アクションとして調整した。`CalendarView` とその子のRectTransformは変更していない。
- `TabletApplicationWindowTransition` を追加し、DiaryIconからCalendarViewへアイコン起点で拡大表示する共通アプリ遷移を実装した。
- `PromptParentPanel` のScene親をWritingViewからDiaryPanelへ修正し、ReWritingButtonで本文・音声入力表示・完了操作を消して選択UIへ戻すようにした。
- DayGridの日付セルは実行時に左上揃えへ統一し、日付は35pt、日記本文プレビューは先頭12文字を16ptでその下に表示するようにした。
- PowerIconでタブレットを消灯した後のDesk会話カメラ復帰は、保存済み姿勢への即時スナップをやめ、既定強さ2の反比例型イージングで補間するようにした。

## 変更した主要ファイル

- `Assets/Scripts/System/OpenBetaTitleBootstrap.cs`
- `Assets/Scripts/System/DiaryCalendarController.cs`
- `Assets/Scripts/System/TabletApplicationWindowTransition.cs`
- `Assets/Scripts/System/TabletHomeApplicationController.cs`
- `Assets/Scripts/System/DiaryJournalStore.cs`
- `.codex/tools/github_issue_progress.py`
- `.codex/tools/tests/test_github_issue_progress.py`
- `AGENTS.md`
- `Assets/Scripts/System/SystemTimedEventCatalog.cs`
- `Assets/Scripts/System/CatPresentationModeController.cs`
- `Assets/Scripts/ActionSystem/FadeController.cs`
- `Assets/Scripts/System/DialogueManager.cs`
- `Assets/Scripts/System/CatPositionController.cs`
- `Assets/Scripts/Conversation/State/ConversationGameStateConditionEvaluator.cs`
- `Assets/Scripts/Conversation/Editor/ConversationDataLoaderTests.cs`
- `Assets/Scripts/TimeSystem/Editor/WeatherSystemTests.cs`
- `Assets/Scripts/Editor/DialoguePreviewWindow.cs`
- `Assets/Scripts/Player/PlayerAreaTracker.cs`
- `Assets/Scripts/Player/PlayerAreaVolume.cs`
- `Assets/Scripts/Player/Editor/PlayerAreaTrackerTests.cs`
- `Assets/Scenes/TitleScene.unity`
- `OpenBetaCanvas/TabletButton`（TitleScene内UI参照）
- `Assets/Timelines/MoveToStart.playable`
- `TalkSource/TalkCSV/DialoguePreview_Integrated.csv`
- `Assets/Resources/TalkData/DialoguePreview_Integrated.bytes`
- `docs/BasicConversationLocations.md`
- `docs/DiaryCalendar.md`
- `docs/DiaryJournal.md`
- `docs/PlayerAreaTracking.md`

## 実施した動作確認・テスト

- Unity MCPでAsset Refresh・再コンパイルを実行し、Console Error 0件を確認した。`PlayerSafetyMovementTests`はEditModeで5/5成功した。GameSceneを再読込後も`部屋床-001`が既存Mesh Colliderを維持して`Walkable` Layerであることを確認した。
- Unity MCPでAsset Refresh・再コンパイルを実行し、`PlayerAreaTrackerTests`はEditModeで2/2、`PlayerSafetyMovementTests`は5/5成功した。Console Errorは0件で、既存の`PlayerLookTarget.DestroyObject`の隠蔽Warning 1件のみが残る。

- `DialoguePreview_Integrated.bytes` を復号し、`00350` のActionが `play_conversation_timeline:MoveToDesk` であることを確認した。
- `MoveToStart` に旧位置更新・終了連鎖Markerが残っていないこと、`MoveEnd` のFadeFromBlack Markerが残ることを確認した。
- `TitleScene` の `MoveToDesk` / `MoveToTable` が同一のStart/End Directorを参照し、遷移先だけ異なることを確認した。
- 会話拠点移動の実装が `FadeController.BecameOpaque` / `BecameTransparent` を待機し、`WaitForSeconds` や `homeLocationChangeTime` に依存しないことを静的に確認した。
- `TabletCameraPoint` のScene参照、指定された位置・回転、`MoveToDesk` のカメラ遷移フラグ、入力・カーソル保存/復帰のコード経路を静的に確認した。
- `TabletButton` のScene参照、Desk・通常入力待ちの可視条件、クリック登録、会話UIの抑止・復帰経路を静的に確認した。
- `TabletDisplay` の `Start Powered On` がオフ、`TabletCanvas` の表示状態が閲覧カメラ到着／終了処理で切り替わることを静的に確認した。
- 会話Action用のStart/End DirectorがPlay On Awake無効、既定の `CatPositionController.currentHomeLocation` がTableであることを確認した。
- `TimeManager` の保存済み開始日から3日目の日付を算出すること、0日以下を1日目へ丸めること、加算不能な日数を `DateTime.MaxValue.Date` に丸めるEditorテストを追加した。
- Unity MCPのスクリプト検証で `DiaryJournalStore`、`DiaryCalendarController`、`SystemTimedEventCatalog` のエラー0件を確認した。`DialogueManager` は既存のUpdate内文字列連結に関するWarning 1件のみである。
- `TitleScene/DiaryPanel` に `WritingView` があり、朝・昼・夕方ボタン、本文、音声入力風インジケータ、終了ボタンがEditor上で編集できることを確認した。
- Unity Play Modeで `DiaryCalendarController` のCalendar/Writing TMP参照、`PromptParentPanel`、`ReWritingButton` がすべて解決されることを確認した。`TabletCanvas` 内は旧 `Text` 0件、TMP 71件である。今回の変更に起因するConsole error/warningはない（既存のOpenBetaCanvas scale情報ログのみ）。
- Play Modeで `TabletCanvas` のTMPテキスト全件が `TabletZenMaruGothicDynamicBitmap` と `TextMeshPro/Mobile/Bitmap` を参照し、旧`Text`が0件、`TMP_SubMeshUI`が0件であることを確認した。ユーザー確認でもテキスト表示が復帰した。
- `CSVCryptoCompiler` でTalkSource配下16 CSVを再暗号化し、`SystemTimedEvent.bytes` を新しい日記CSVヘッダーと同期した。
- 今回の対象C#・文書に対する `git diff --check` を実行し、改行形式のWarning以外の空白エラーがないことを確認した。
- `TabletCanvas` のTransformが左右反転しないY=180回転とDisplayArea表面のカメラ側となるZ=+0.054を保持し、`EmissionPanel`（Z=+0.051）より前面にあることを静的に確認した。`OpenBetaTitleBootstrap` にDisplayArea Rendererを無効化する参照・処理が残っていない。
- Unity MCPで開いているTitleSceneのTabletCanvasが一時的に旧Z=-0.054のまま残っていることを検出し、Z=+0.054へ直接更新してScene保存した。`TabletDisplaySceneRegressionTests` はEditModeで3/3成功した。
- Unity Play Modeで、黒画面時の`TabletCanvas`がActiveにもかかわらず`CanvasGroup.alpha=0`であることを確認した。`TabletDisplayController`との二重状態管理が原因であり、電源ONでCanvasGroupを不透明へ戻すよう修正した後、同じPlay視点で正しい左右向きの画面描画と`CanvasGroup.alpha=1`・EmissionPanel有効をスクリーンショットで確認した。PowerIconのクリック後は、`TabletCanvas`と`EmissionPanel`がともに`activeSelf=false`となり、通常視点へ復帰した。終了後のConsole Errorは0件。
- Issue #4では、Scene YAML上でPromptParentPanelの親がDiaryPanel、WritingView側にPromptParentPanelの子参照がないこと、手動起動が共通アイコン遷移、夜起動・保存後のカレンダー復帰が非アイコン遷移を使うことを静的に確認した。書き直し時の表示初期化、Scene親子関係、入口分離を検証するEditorテストを追加した。
- UnityのEditor.logで今回の`BuildCalendar`任意引数による既存ボタン登録の型エラーを検出し、ラムダで明示して修正した。Unity MCP再接続後にAsset Refresh・再コンパイルを完了し、修正後のUnityコンパイルと追加テストの成功を確認した。
- Unity MCPを再接続してAsset Refresh・再コンパイルを実施し、Console Error 0件を確認した。`TabletDisplaySceneRegressionTests`（5件）と書き直し表示初期化テストを含むEditModeテストは6/6成功した。Play Modeでは、画面点灯後のDiaryIcon手動起動がCalendarViewのみを有効にして共通遷移中になること、夜起動がCalendarView/WritingViewを閉じたままPromptParentPanelを表示すること、実際のReWritingButtonイベント後にPromptParentPanelの親がDiaryPanelで本文・マイク・完了/書き直し操作がすべて初期化されることを確認した。検証用スクリーンショット2枚はAssetDatabaseから削除済みである。
- DayGrid日付セルの左上揃え、35pt日付、先頭12文字プレビューを検証する回帰テストを追加し、`TabletDisplaySceneRegressionTests` はEditModeで6/6成功、Console Error 0件を確認した。
- PowerIcon復帰用の反比例型イージングは、0・中間・終端の値と、PowerIcon経路がこの補間を要求することを回帰テストで検証し、`TabletDisplaySceneRegressionTests` はEditModeで7/7成功、Console Error 0件を確認した。Play ModeでもPowerIcon経路を起動し、消灯開始時点でタブレット姿勢を保持してからDesk復帰へ進むことを確認した。
- Unity MCPを再接続し、ポート6401のフレーミング接続で`ping → pong`を確認した。MCP経由でAssets/Refreshを実行して新規Controllerを再コンパイルし、`TabletHomeApplicationController`に関するConsole Errorは0件だった。`TabletDisplaySceneRegressionTests`はEditModeで11/11成功した。
- 会話拠点移動が完全暗転イベント同期を維持しつつ、専用の暗転・明転各0.5秒設定を使用することを静的に確認した。
- `dotnet build Assembly-CSharp.csproj --no-restore` は、変更箇所ではなくPackageCacheの既存UGUI `MenuOptions.cs` における `DefaultControls.factory` の読み取り専用代入エラー2件で失敗した。`--no-dependencies` はUnity生成済みTemp DLL不足で検証できなかった。ユーザーが起動中のUnity Editorは停止していないため、batchmode実行は行っていない。
- `github_issue_reader.py` のテスト9件（日本語の「タスク番号」指定を含む）と、進捗ラベル遷移・完了報告・Closeを検証する `github_issue_progress.py` の新規テスト4件が成功した。実際の`start --issue 5`は、GitHub CLI・`GITHUB_TOKEN`・`GH_TOKEN`がいずれもない環境で安全に失敗し、認証値やAPI詳細を出力しないことを確認した。その後GitHub CLIを導入してブラウザ認証を完了し、Issue #1〜#6について着手・完了の進捗遷移、作業結果コメント投稿、最終的な`codex-review`ラベルをGitHub APIで確認した。
- Issue #6の着手ラベル更新は、GitHub CLI・`GITHUB_TOKEN`・`GH_TOKEN`がない環境で安全に失敗し、認証値やAPI詳細を出力しないことを確認した。MCP再試行は、意図的に失敗するローカル接続の後で新しいUnity MCP接続を確立して`ping → pong`を確認した。
- タブレット修正後、Unity MCPを再接続・再試行付きで利用して`Assets/Refresh`を実施した。初回確認時に、Nyansta/Shop/DemaePanelのCanvasGroup欠落による`TabletApplicationWindowTransition.cs:254`のMissingComponentExceptionを各1件検出し、CanvasGroupの取得・追加を安全化して再読み込みした。`TabletDisplaySceneRegressionTests`はUnityがPlay Mode中のため起動を拒否され、Play Mode終了後の再実行が必要である。
- `DiaryPanel/DetailView` の `DetailTitle` は選択日を `M/dの日記` 形式で表示するようにし、`BeforeDayButton` / `NextDayButton` で前後の保存済み日記へ移動できるようにした。記録がない日付は飛ばし、端ではボタンを無効化する。
- この日記詳細変更後、Unity MCPのAssets Refreshで再コンパイルし、Console Error 0件を確認した。`TabletDisplaySceneRegressionTests` はEditModeで16/16成功した。
- DiaryPanelの`RectMask2D`遷移表示領域を確認するEditMode回帰テストは1/1成功し、Unity Console Errorは0件だった。
- Issue #8の特別日付重複排除・昇順・Detail日送り、PlayerPrefs解禁、仮特別日記と肉球リソースを確認する回帰テストを追加した。`TabletDisplaySceneRegressionTests`はEditModeで23/23成功、Unity Console Error 0件だった。CSV再暗号化後、`TEMP_SPECIAL_DIARY_001`は`SystemTimedEventCatalog`から特別日記として本文・IDともに読込成功した。

## 未解決事項

- #9の実機確認は完了しているが、現在のPlayer一式は`TitleScene`に保存され、GameSceneには存在しない。本番対象をGameSceneにする場合は、明示的にPlayer・Walkable床・AreaVolumeを配置または移動し、#9の安全移動を再確認する必要がある。対象Sceneが未確定のため自動移動はしていない。
- 本番Playerへ`PlayerAreaTracker`を追加し、必要な場所に`PlayerAreaVolume`を配置して、Area Id・Priority・Resolve Orderを設定したうえで、実プレイ中の出入り・重複領域の優先順位を確認する必要がある。Scene自動編集はしていない。

- Unity Editor上で実プレイし、Choice承認時に暗転、完全暗転イベント後のDesk移動、`H: Desk` 会話への切替を確認する必要がある。
- Unity Editor上で実プレイし、`MoveToDesk` / `MoveToTable` で完全暗転後にだけ猫の配置・向き・姿勢・視線が更新され、明転後に猫が表示されてから必要時のタブレットカメラ補間が始まることを確認する必要がある。
- Unity Editor上で実プレイし、MoveToDesk後のタブレットカメラ補間、カーソル解除、および `ExitTabletCameraView()` による復帰を確認する必要がある。
- Unity Editor上で、Desk・通常入力待ちだけTabletButtonが表示され、押下中・閲覧中は隠れ、復帰後に再表示されることを確認する必要がある。
- Unity Editor上で、DiaryIconから日記を開き、月送り制限、日付セルのプレビュー／本文、`AddEntry(string)` の保存・再表示を確認する必要がある。
- Unity Editor上で、Diary DetailViewの `9/21の日記` 形式のタイトルと、前後の日記ボタンの遷移・端での無効化を確認する必要がある。
- Unity Editor上で、Issue #7のCalendar→Detail、Calendar→Writing、Writing/Detail→Calendarの左右スライドと、`DetailBackImage`だけが動く日送り、遷移中の入力ロック、および画面外へページが描画されないことを実プレイ確認する必要がある。
- Unity Editor上で、特別日記取得直後に肉球付きソートボタンが解禁され、通常月間表示の肉球、特別ソートの1日1セル、特別日付だけの日送り、Detail→Calendarでのソート状態維持を実プレイ確認する必要がある。
- Unity Editor上で、Nyansta・Shop・demaeがアイコン位置から全画面へ拡大し、Diaryを開閉した後も各アプリPanelが全画面の正しい座標で開くことを確認する必要がある。Play Mode終了後に`TabletDisplaySceneRegressionTests`を再実行する。
- Unity MCPブリッジを再接続後、`UiInteractionRegressionTests.ApplyTree_LeavesTabletButtonOutsideAutomaticButtonConversion` を実行し、Console Error 0件と合わせて確認する必要がある。
- 現在のPlay Mode Consoleには、今回未変更の`NekomataLookRigController.cs:1255`（`RigBuilder.Build()`）でのNullReferenceExceptionが1件ある。タブレット関連のConsole Errorは0件だが、別タスクでRig参照の初期化状態を確認する必要がある。
- Unity Editor上で、夕方から夜へ進む時間経過イベント後のDesk・タブレット遷移、通常草稿の選択・音声入力風の文字送り、書き直し後の草稿保持、終了による保存、死亡を含む複数Pendingの発生順記入と翌日以降の保持を手動確認する必要がある。
- Tableへ戻す会話を作成する場合は、CSVのActionを `play_conversation_timeline:MoveToTable` にするだけでよい。Timeline・Director・Signalの追加は不要。

## 今回確定した仕様・判断

- プレイヤー安全移動はRigidbodyへの移行や現在地Condition統合を行わず、写真撮影用`FreeCameraController`と別の`PlayerSafetyMovementController`として実装する。Walkable Layer名はコードへ固定せず、InspectorのLayerMaskで指定する。
- 現在地判定は安全歩行から分離し、Trigger通知を補助として使いつつAreaVolumeの毎フレーム再評価を正本とする。Dialogue Condition、ケーブル移動、ネルコ搭乗・追従は接続しない。

- 場所移動の現在地更新とStart→End連結はコードが担い、Signal Receiverは共通の暗転・明転用途に限定する。
- 位置ConditionはTransform検索や毎フレーム距離判定を行わず、`CatPositionController.CurrentConversationLocation` の状態のみを参照する。
- タブレット閲覧カメラの姿勢はScene上の `TabletCameraPoint` を正本とし、場所移動の明転完了後にだけ起動する。
- ChatGPT側で外部作業指示用のGitHub Issueを作成する際は、`codex-task` ラベルを必須とする。ラベルなしIssueはCodexの自動作業指示として取得されない。
