# 開発引き継ぎ

> 現在状態のみを記録する。秘密情報、APIキー、個人情報、会話・Issueの全文は記載しない。

## 更新日時

2026-10-14 JST

## 現在の作業

ユーザー直接指定#58（Open・ラベルなし）は許可に基づき実装・検証完了。Window > Nekolpos > Timeline Paletteで既存Signal5件を自動収集し、名前/パス検索・カテゴリ・再生ヘッドLocal時刻へのクリック配置・Track自動作成/指定・一括Undo/Redoを提供。既存Receiver登録済みTrackを優先し、親を含むロック/ミュートを保護。Receiver/Binding/既存Marker・Scene・Runtime・台詞は変更しない。公開APIではドロップ座標→時刻取得が内部API依存のため、Issueの許容どおり再生ヘッド配置を正式仕様とした。新規: Assets/Scripts/Editor/NekolposTimelinePalette.cs、NekolposTimelinePaletteTests.csと各meta、docs/TimelinePalette.md。正式EditMode10/10成功、失敗/Skip0（実TimelineウィンドウのLocal再生ヘッド・Undo/Redo・Binding保全・無効時刻/参照/Trackを含む）。MCP job 9a0f7f91f93043e19ed5b25efcb76b32。Unityでパレットを開いた。MainScene dirty=false・Play停止・Console Error0。テスト用Asset削除済み。実際のマウス操作による検索/配置・演出再生の見た目確認はユーザー側に残る。GitHubラベル/コメント/Close・コミットは行っていない。

ユーザー直接依頼#74〜#80の7段階を実装・検証完了、全7Issueへ完了報告投稿済み、Open/codex-review（Closeしていない）。F1共通Console/固定Prefab、共有入力Lease/安全ガード、既存DebugPanel統合と正規運搬、診断Trace/JSON、Scene Awake前から終了までの空メモリ保存隔離/猫Asset複製/副次保存抑止、許可IDのジャンプ・日記・運搬・死亡、Editor共通Runnerと終了後ハッシュ監査を導入。現行MainSceneにGameManager/ConversationDebugPanelはなく、入口はBootstrap実開始状態。未接続Managerは新設しない。

検証: 正規EditMode232/232成功（実Play遷移含む）、最終の入力共有/破棄/既存Pause30/30成功、失敗0/Skip0。新規隔離Playの8シナリオはすべて自動Passed、各13〜15チェック成功、通常保存/Prefs/元CatDataSO/未保存Sceneを保全。全対象モードのConsole開閉・外部IME変換フラグ拒否・Cursor/Pause/TimeScale・有効なUI選択復帰、日記2種の実Button確定/カレンダー保存/Pending消費/就寝翌朝+1、運搬Preset、死亡翌朝+1を確認。Console表示中のタイトル/Scene復帰でもUI1個・所有解放・カーソル解放/表示・TimeScale1・タイトル拒否・保存保全。描画/実キー/実IME/Windows・WebGL・モバイル実Player操作は要手動確認。

Windows Release実BuildはSucceeded、31.21秒、2209.39MB、Error0/Warning0（15:52:39Z完了）。配布DLLのConsole/シナリオ/旧会話Debug変更API不在をメタデータで検証。WebGL ReleaseとWindows DevelopmentのRuntime条件コンパイルはError0（Inspector注入フィールド等の警告は実Buildと別集計）。WebGL実Build/ブラウザ・Development実Player未実施。出力Build/DebugConsoleRelease、レポートLibrary/DebugConsole。

初回の旧統合テストが通常日付/草稿1件/会話ログを残した副作用を検出。統合テストを隔離Playへ変更し、旧UnknownWord時間進行の期待を現行へ修正。通常日付/草稿/会話ログは作業前SHA-256に一致する元データへ復元、追加PerfDiag14件をLibraryへ退避。通常ゲームデータ/猫Asset差分0を再確認（Unityセッション値・NUnit XML/PT_Runは成果物）。通常学習辞書の既存Invalid answerは未変更、別途整合確認事項。

主要変更: Assets/Resources/Debug/NekolposDebugConsole.prefab＋meta、SystemのNekolposDebugConsole/DebugConsoleInput/Guard/Trace/Snapshot/DebugSaveSession/DebugScenarioCatalog/Fixtures/Executor＋meta、EditorのAuthoring/SessionMenu/NormalSaveAudit/ScenarioEditorRunner/ReleaseProbe＋meta、新規Debugテスト4ファイル＋meta。既存DebugPanel/ConversationDebug/入力各経路、Bootstrap/Pause/Chat/DialogueEngine/DialogueManager/Diary/Status/Time/ProgressSaveStoreと副次保存各経路へ最小接続、既存統合テスト2件とRelease Sanitizerを更新。仕様docs/DebugMode.md、操作docs/ControlGuideHUD.md。既存ユーザー変更は保持、Scene自動保存・台詞生成・コミットなし。

Unity6000.3.10f1、UnitySkills RESTは3回確認で不応答→Unity MCPでEditor/正式Runner/Build代替（REST固有は未確認）。Domain Reload後にMCP Job表示が停滞した回は実NUnit XMLを完了正本とする。証跡output/debug-console/regression-final.xml/lifecycle-final.xml、Library/DebugConsole/batch-summary.json/Reports/normal-full-recovery.json/title-return-result.json/windows-release-verification.json/ReleaseProbe。最終Play停止/compiling=false/Scene dirty=false、Console Error0。通常保存・会話履歴・元猫Assetは最終監査でも作業前ハッシュ一致（差分0）。

## 現在の状態

- #71: 実装・検証完了。MainSceneへ固定学習フォームと言葉メモ配置済み。既存ユーザー変更を保持、Play停止/Scene保存/Console Error0。

- 今回の#61/#60は実装・正規Runner検証完了、GitHubは両方Open/codex-review。Console Error0、MainSceneのPlay停止/compiling=false/Scene dirty=false。

散歩視線/Tablet追加修正: NekomataLookRigControllerはWalkMode中にPlayerWalkModeController.WalkCameraを優先し、会話用Main CameraB参照・オフセット・固定距離・Idle Random Lookを散歩に持ち込まない。両目/Debug Aimは実Camera位置、首は実行時GeneratedNekomataWalkHeadAimTargetで既存Yaw/Pitch制限を維持する。通常会話/Editor Previewは従来経路。PlayerWalkModeのTabletInteractionChangedをCatLocomotionが購読し、入口で同期的にAgent停止、閲覧中は現在位置でFollow/Waitを保持。BootstrapのPowerOffTabletAndReturnToDeskRoutineはWalkMode入口のSetHomeLocation(Desk)を省く。変更: NekomataLookRigController、PlayerWalkModeController、CatLocomotionController、NekolposGameBootstrap、既存テスト2ファイル、CatLocomotion/TabletPowerControl/SmokeTest仕様。MainScene PlayでTable/FloorのAim-Camera差約2.8e-7/1.8e-7m、両目角度差0度。Floor視線検証はSafetyを一時無効にして低い位置へ固定。Floor/Wait Tablet電源終了74フレーム、Table/歩行中Follow開始から電源終了187フレームで猫の位置/回転差0。通常会話入口ではDesk復帰も確認。Editor直接実行回帰4ケース成功（Test Runnerではない）、最新Console Error0、Play停止済み。Sceneを書き換えず未保存変更を保持。人による見た目確認は残る。

Followアニメーション追加修正済み。待機loop_1→belly_end→Walk_F_IP→belly_start→loop_1を既存CatLocomotionControllerで接続。散歩開始・再開始の伏せ状態を登録し、Waitでは起き上がらず伏せを維持。歩行停止時は即belly_startへ進むため旧Near Rest Delayを廃止。Start/End終盤の残りBlend時間（0.15秒）でCrossFadeし、立ち上がり終盤までAgent停止を保持。AnimatorのWalk_F_IP Stateは空Blend Treeだったため既存実Clipへ割当て保存。Controller資産は既存Assets/DL AssetsのGit除外対象（ローカル保存済み）。MainScene Playの実Clip・座標で遷移を確認し、終盤norm0.85〜0.88で次Stateへ、歩行約0.20m、Wait移動0。最終コードの関連7ケースをEditorから直接実行し全成功（Test Runner実行ではない）。未保存MainSceneは勝手に保存せず保持、コピーをC:/Users/shoho/.codex/tmp/follow-animation/MainScene-before.unityへ退避。人によるブレンドの見た目確認は残る。Smoke中にWaypoint Marker(Clone)のDistanceTextUIでTextMeshProUGUI参照未設定Error1件を検出、Follow外の未解決事項として記録。

#28追加修正: 散歩開始・再開始はFollow。「ここで待ってて」はWait、「付いてきて」はFollowへ戻す。MainScene/Table内で0.55m以上離れると移動する。旧0.75mでは現在のTable開始位置からの完全Path最遠点約0.665mに届かず、Followを選んでも静止していた。停止距離0.45mは維持する。Table/Deskの立位CharacterController中心が従来Area Box上端より高く、Area=Noneになっていたため、上端のみ8cm拡張し水平範囲・下端を維持した。MainScene設定は保存済み。未保存Sceneの修正前コピーはC:/Users/shoho/.codex/tmp/nek2829smoke/MainScene-before.unityに退避済みで、元のユーザー変更も保持して保存した。

Smoke中のScript再読込不具合も修正済み。WalkDialogue.OnEnableでCatalogを再取得。LookRigのRigLayerにisInitializedだけ復元されconstraintsが欠落した場合、Rig/activeを維持して未初期化Layerへ置換する。Scene退出時のCatLocomotion→Presentation呼出しはUnity null比較で破棄済み参照を除外する。詳細: docs/CatLocomotion.md、docs/SmokeTest-20261003.md。

#31はTimeManager.CurrentDayPrefsKeyとして既存Nekolpos.Debug.StartDay.v1を継続使用する。日送りと明示日数変更で保存。未保存・0以下・DateTime範囲外はDay1。再開では朝を正本とし、前セッションのGameManager時刻を初期化へ取り込まない。保存日は通常Startでも既存OP最終配置／通常カメラ経路へ進む。日記の日付計算は既存TimeManagerの開始日＋日数を共有する。#33はOpenBetaPauseMenuController.ResetOpenBetaStateでBootstrap、会話Sequence、日記、タブレット、エリア移動、散歩、Prompt、Fade、Chat/Choice/Log、Pause、入力・Cursor・TimeScaleを解除する。場所のSingle Scene遷移では会話データの初期化をせず一時状態のみ解除する。Fadeは完了イベントを発火させずキャンセルし、移動の旧awaitは世代番号で復帰を抑止する。WalkDialogueCanvasは所有PlayerのScene寿命に合わせる。詳細はdocs/DiaryCalendar.md、Windows監査A〜Dはdocs/WindowsStandaloneAudit-20261003.md。


Issue #27の案内CanvasはPlayerと同じSceneに生成し、DontDestroyOnLoadにしない。`PlayerInteractor.ResetInteractionState()`が対象参照・テキスト・表示を解除し、散歩終了、Disable/Destroy、Active Scene変更、タイトル復帰前に適用する。Pause中は検出を停止しResumeで再検出する。Issue #28はMainSceneのController／Choice／NavMesh接続が正常であることを確認した。目的地の手前オフセット0.4mとAgent停止0.4mの二重加算を、目的地0.4m＋Agent停止0.05mへ修正した。休止終了中はCoroutine完了を待ち、毎フレーム再開始しない。停止・休止中もLocomotionのAnimator所有権を維持し、散歩終了時は確実に返す。InspectorへPlayer距離・NavMesh所属・Path状態・残距離を追加した。Issue #29は`TabletEntryContext`でConversation/WalkModeを区別し、散歩開始直前のPlayer World位置・回転とCamera World/Local姿勢を保存する。復帰補間は保存した散歩Cameraへ向け、会話CameraはPlayerCameraへ切り替えた後で保存姿勢へ戻す。Safetyの`TeleportTo(..., false)`と`SetLookRotation()`で安全位置を上書きせず復帰し、Area・視点内部角度・入力も同期する。会話・夜の日記の入口は従来経路を維持する。

ニャンスタグラム実猫写真基盤は、`tools/nyanstagram/input`の原本、私用の変換マニフェスト、`Assets/StreamingAssets/Nyanstagram/Local`の生成JPEG・実カタログをGit対象外にする。変換ツールはEXIF Orientation補正、RGB化、長辺1024px上限、品質82、メタデータ非継承、`CAT_000001.jpg`形式の内部ID出力を行い、原本を変更しない。READMEはプロジェクト直下用と`tools/nyanstagram`内用の両方の実行コマンドを記載する。Unity側は`INyanstagramPhotoSource`を取得元の境界とし、現在の`LocalNyanstagramPhotoSource`だけがStreamingAssetsを読む。将来のRemote/Cacheは同インターフェースを実装して差し替え、UI・日付更新ロジックへ取得元の分岐を持ち込まない。UIとRemote通信は未実装である。変換のPython自動テスト2件、UnityのAsset Refresh後のConsole Error 0件を確認済みである。

`CableTravelInteractable` は、Inspectorの`Prompt Text`、`Destination`、`Expected Area Id`、`Area Transition Controller`、`Walk Dialogue Controller`、`Walk Dialogue Interaction Id`へ日本語Tooltipを付与済みである。Cable移動時に`PlayerAreaTransitionController`が暗転完了を確認できず中止した原因は、参照先`FadeController`の`rootCanvas`未設定でOverlayを生成できなかったことだった。`FadeController.EnsureUi()`は同一GameObjectだけでなくシーン内の`ChatUIController.messageText.canvas.rootCanvas`、最後にCanvasを解決するよう修正済みであり、暗転完了後だけ移動する安全条件は維持する。

Deskの通常会話で表示する`TabletButton`は、MainSceneに`Button.interactable = false`として保存されていた一方、`RefreshTabletButtonAvailability()`がactive状態だけを更新していたため、表示中でもクリックを受け取れなかった。同メソッドは表示可否と同じ`CanOpenTabletFromButton()`の結果を`interactable`にも同期するよう修正済みである。タブレット閲覧のカメラ遷移・入力抑止・既存表示条件は変更していない。

卑猥語の検知CSV `VulgarLanguage 2fd3350ea12d8001872cf0e28346f230.csv` は「単語,宛先ラベル」の2列構成として`DialogueEngine.LoadVulgarStyleCsv()`が読み込む。`FindBestMatch()`は最長検知語に対応する宛先ラベルHashを使うため、`ちんちん`／`まんこ`などは`ひわい`（`-462947388`）へ遷移する。宛先列のない旧CSVは既定で`ひわい`へフォールバックする。既存の00670反応14行は本文を変えず`げひん`から`ひわい`ラベルへ移し、TalkTopicMasterも同期済みである。`DialoguePreview_Integrated.bytes`を再暗号化し、復号検証で`00670,ひわい,ひわい`が14行入っていることを確認した。`DialogueEngineMatchPriorityTests`には第2列の宛先ラベル反映を検証するEditModeテストを追加した。Unity実機で卑猥語入力時の実際のランダム反応を確認する必要がある。

ゲーム開始時の通常会話UIは、`PlayerWalkModeController.ApplyNormalConversationState()`が`CursorLockMode.None`と`Cursor.visible = true`を明示する。これにより散歩やPlay再開で残ったカーソルロックを引き継がず、開始直後からUIをクリックできる。

Escポーズメニューから開くログ送信・確認画面とアンケートは、ポーズメニューを閉じずにその配下の操作として扱う。`OpenLogFromMenu()`は`CloseMenu()`を呼ばず、ポーズ状態・TimeScale停止・散歩入力停止を保持したままログを前面表示する。ポーズ開始および外部アンケート起動時にはカーソルを表示・解放するため、遷移先でもクリック操作を継続できる。

会話拠点のTable/Desk移動後は、`CatPresentationModeController.ResumeIdleAfterTimeline()`がPresentation側のIdle Variationを再開する。MainSceneの`CatPresentationModeController`は`CatSimple_Lie_belly_loop_2`／`CatSimple_Lie_belly_loop_3`を有効にしており、`NekolposGameBootstrap`は暗転中の配置後とEndOfFrame後に`RefreshLookAtCameraImmediately()`を実行するため、視線Rigも復帰する。`CatAnimationRuntime`のMainScene設定は`enableIdleVariations = false`なので、拠点移動完了時に`ResetToDefault()`を呼ぶと`loop_2 / loop_3`と視線を止めてしまう。通常完了・FadeController欠落・途中停止はいずれもPresentation側復帰に統一した。Unity実機でTable/Desk移動を再生して最終確認が必要である。

`PlayerSafetyMovementController` は通常プレイ専用の`CharacterController`移動として、Inspector設定可能な`Walkable Layers`と最大傾斜によるSphereCast接地判定、進行先の足場確認による端落下防止、安定接地後だけ保存する`LastSafePosition`、異常落下時の復帰を提供する。写真撮影用`FreeCameraController`とは分離した。身長5cmの初期値は高さ`0.05`、半径`0.01`、`center.y=0.025`、`stepOffset=0.01`であり、子カメラの目線高は`0.043〜0.045`を目安にする。`SetMovementInput`はモバイルUIスティックなどの入力注入用であり、従来のHorizontal/Vertical軸は入力Override未設定時だけ使う。`SetEdgeFallPreventionEnabled`、`SuspendSafetyRecovery`、`ResumeSafetyRecovery`、`TeleportTo`、`SetLastSafePosition`、`ResetLastSafePosition`をイベント・遷移側の公開APIとする。`Walkable` Layerは`TagManager.asset` index 7に定義済みである。空中落下は`Max Fall Speed`で下限クランプし、重力`9.81`と接地時の`Grounded Downward Speed`は変更しない。MainSceneの値`2`は保存済みで、`PlayerSafetyMovementTests`はEditModeで5/5成功した。#9のユーザー実機確認では、通常歩行、壁衝突、床抜け防止、LastSafePosition復帰、崖端停止が成功している。壁密着時にカメラ越しに壁の向こう側が見える現象は、Near Clip Plane最小値でも残る別課題として保留している。現在の`Player`一式は`MainScene`にのみ保存され、GameSceneのScene YAMLには存在しない。

`PlayerAreaVolume` はInspector設定可能な`Area Id`、`Priority`、`Resolve Order`とTrigger Colliderを保持する。`PlayerAreaTracker` はCharacterController中心（なければTransform位置）を使い、毎フレームAreaVolume候補を再評価するため、CharacterControllerのTrigger通知が不安定でも現在地を保持できる。重複時はPriority、Resolve Order、Area Idの文字列順で決定的に選び、`CurrentAreaId`、`CurrentArea`、`IsInArea`、`RefreshAreaNow`、`AreaChanged`で外部連携する。未所属は`None`であり、同じArea Idの継続ではイベントを再発行しない。

Waypoint表示を静的・Play中に調査した結果、MainSceneの`WaypointUIManager`は`WaypointSettings3D`、`Marker.prefab`、`OpenBetaCanvas/WaypointMarkers`を正しく参照している。保存済みSceneでは`CableInteract_Desk`を含む4地点の`WaypointTarget.preset`はnullである一方、実行中の`CableInteract_Desk`には`Assets/DL Assets/WaypointSystem/Demo/MainPreset.asset`が設定されており、同時に生成された`Marker(Clone)` 2個のうち一方が赤（1, 0, 0, 1）、他方が黄色（約0.873, 1, 0, 1）だった。Deskだけ赤い直接原因は、この実行時Preset設定である。Play中の変更は停止時に戻るため、停止後にDeskの`WaypointTarget > Preset`をNoneへ戻して保存するか、赤を意図するならMainPreset側の色を黄色へ統一する。`WaypointMarkerUI`はAwake時の色をdefaultColorとしてキャッシュするため、Prefab色を変える場合はPrefab ModeでApply/Save後にPlayを再起動する。Managerの`Waypoint Camera`は散歩時だけ有効になる`PlayerCamera`で、`Use Off Screen Indicators`はオフである。

Issue #22では、`MainScene/Player/PlayerCamera`へ`PlayerCameraWallCollisionController`を追加し、`1Room(軽量版)/壁-001`の既存MeshColliderだけを`Wall Root`に設定した。散歩中にPlayerCameraが有効な時だけ、8方向の小さなSphereCastで壁へ近づく前に表示位置を手前へ補正し、壁から離れると平滑に元へ戻す。Near Clip Planeは変更しない。`Main CameraB`／`CameraEventPivot`および通常会話・OP・タブレット時には補正を即時復元して作用しない。初期値はProbe Radius `0.003m`、Wall Clearance `0.016m`、Collision Smooth Time `0.015秒`、Return Smooth Time `0.06秒`。新規の補正量EditModeテストを追加し、両C#ファイルのUnity検証でエラー0件を確認したが、Unity Test Runnerはテスト開始前の初期化タイムアウトで未実行のため、実機で正面・斜め・角・壁沿い移動と既存カメラ遷移を確認する必要がある。

Issue #23のネルコ移動基盤は`CatLocomotionController`として`Normal_Cat`へ追加・保存済みである。WalkMode中かつPresentationがConversation/Idleの時だけ猫用`NavMeshAgent`と移動アニメーションを所有し、「付いてきて」でPlayer手前の停止距離を保って追従、「ここで待ってて」でその場待機する。追従開始`0.75m`・停止`0.45m`のヒステリシス、再経路間隔`0.25秒`・Player移動閾値`0.08m`を持ち、`CatMotionController.SnapToHome`は使用しない。FollowStopDistance内ではPath・速度を解除してbody/rootをPlayerへ回転追従させず、既存LookRigだけを維持する。2.5秒の近距離停止後は非ループの`CatSimple_Lie_belly_start`を完了して既存Stateの`CatSimple_Lie_belly_sleep`へ移り、FollowStartDistanceを越えると非ループの`CatSimple_Lie_belly_end`を完了して通常Walkへ復帰する。Start Clipは既存FBXからNormalCat Animator ControllerへState登録済みである。Slow Walk／Turn In Place／Runは使わず、MainSceneのAgent Angular Speedは120度/秒へ下げた。Area変更は追従中かつ完全経路がない時だけTable/Desk/Floor AnchorへWarpし、経路・速度をリセットして視線Rigを再評価する。待機中はPlayerが別Areaへ移動してもWarpしない。`com.unity.ai.navigation 2.0.15`と猫専用Agent Type `Neruko`を導入し、MainSceneのWalkable LayerからFloor/Table/Deskの3 Surfaceをベイクした。家具MeshCollider上面は独立領域として安定生成されないため、Table/Deskには天板内側のBoxCollider Proxyをベイク入力に使い、各Surfaceの`Min Region Area`を`0.01`に設定した。3 Anchorから対応Player地点までの経路はすべて`PathComplete`である。Issue #23には`codex-task`ラベルがないため、GitHub進捗ラベル変更・完了コメントは行っていない。

Issue #24では`CatMotionController`に、ホームへSnapせずIn-Place Animator Stateを再生する`PlayLocomotion()`と、位置を動かさずIdleへ戻す`StopLocomotionAndReturnToIdle()`を追加した。`IsPlayingLocomotion`と`CurrentLocomotionState`は会話Motionの`IsPlayingMotion`と分離され、会話Motion開始時はLocomotion状態を解除する。会話Motion中のLocomotion再生およびLocomotion非実行時の停止要求は拒否して既存Motionを上書きしない。Root Motionは従来どおり常にOFFで、APIはLookRig／Presentationの有効状態を直接変更しない。Normal_CatのControllerに登録された歩行Stateは`CatSimple_Walk_F_IP`と`CatSimple_Walk_B_IP`、Idleは`CatSimple_Lie_belly_loop_1`〜`_3`である。素材にある左右・斜め・Run／RunFast In-Place Clipは現行Controllerに未登録のため使用しない。詳細と接続時の所有権ルールは`docs/CatLocomotion.md`へ記録した。Issue #24にも`codex-task`ラベルがないため、GitHub進捗ラベル変更・完了コメントは行っていない。

Issue #25では、`NekolposGameBootstrap`が`TabletCanvas`のワールド姿勢・寸法から`RuntimeTabletScreenWakeTrigger`を実行時生成する。Playerには`TabletWakeSource(Player)`、通常猫の前後の足`foot_f.L`／`foot_f.R`／`foot_b.L`／`foot_b.R`だけには小さなTriggerの`TabletWakeSource(CatPaw)`を付与するため、胴体・頭部・尻尾では点灯しない。`TabletScreenWakeController`は接触開始だけを共通`WakeTabletDisplay()`へ渡し、全接触が離れた後は`Tablet World Wake Sleep Seconds`（初期値8秒）で消灯する。タブレットCamera操作中の自動消灯はしない。点灯中は既存`PlayerInteractor`の`TabletWorldInteractable`が`E：操作する`を公開し、アプリPanel・アプリ内状態を変えずに既存タブレットCameraへ移る。散歩から開いた時だけ`PlayerWalkModeController`がPlayerCamera・移動を一時停止し、PowerIconで戻った後は会話へ戻らず散歩へ復帰する。新規EditModeテスト4/4と既存`TabletDisplaySceneRegressionTests` 27/27が成功し、Unity Console Error 0件、Unityコンパイル成功を確認した。Issue #25にも`codex-task`ラベルがないため、GitHub進捗ラベル変更・完了コメントは行っていない。

Issue #13の`PlayerAreaTransitionController`はPlayerと同じGameObjectの`PlayerSafetyMovementController`・`PlayerAreaTracker`を参照し、MainSceneではOpenBetaSystemsの保存済み`FadeController`をInspector参照する。`TeleportImmediately`は既存の完全暗転中に使い、二重フェードせず、安全移動を一時停止して`TeleportTo(..., updateSafePosition: true)`、`RefreshAreaNow()`、状態復元を行う。`TransitionToAsync`は自身でフェードを所有し、`FadeOutAsync()`完了後かつ不透明確認後にだけTeleportする。外部が既に停止していたSafetyRecovery・移動Controllerは再開しない。`IsTransitioning`で多重要求を拒否し、expectedAreaId不一致はWarningのみでロールバックしない。`PlayerAreaTransitionControllerTests`はEditModeで4/4、既存`PlayerAreaTrackerTests`と`PlayerSafetyMovementTests`は合計9/9成功、Unity Console Errorは0件である。

Issue #14では`ConversationTimelineBinding`に`Player Destination`と`Expected Player Area Id`を追加した。MainSceneの`MoveToDesk`は`PlayerTransitionPoints/PlayerSpawn_Desk`と`Desk`、`MoveToTable`は`PlayerTransitionPoints/PlayerSpawn_Table`と`Table`をInspector参照する。会話移動は既存フェードの完全暗転後、猫・会話カメラの更新を行ってから`TeleportImmediately`でPlayerを移動する。Anchor未設定・Controller未設定・多重遷移ではWarningのみとして、猫側の既存フローを継続する。猫拠点とPlayerAreaは統合しない。新規EditModeテスト4/4と、既存Player遷移・AreaTracker・安全移動・Bootstrap回帰36/36が成功し、Unity Console Errorは0件である。

Player自由歩行・Area・意図的移動の品質監査を実施した。RuntimeでPlayer自身の座標を書き換える経路は`PlayerSafetyMovementController.TeleportTo`（安全復帰も同API）だけであり、会話のTable/Desk移動は`PlayerAreaTransitionController.TeleportImmediately`を経由する。見つかった他の座標代入は猫・視線Target・写真撮影・UI・Editor用またはテスト用で、Playerの移動経路ではない。`PlayerAreaTransitionControllerTests`は通常遷移後にSafetyRecoveryと移動Controllerが復元されることを追加検証し、`NekolposGameBootstrapPlayerTransitionTests`は保存済みMainSceneのBootstrap参照とMoveToDesk/MoveToTableの非null Anchor・Area Idを回帰検証する。関連EditModeテストは56/56成功、Unity Console Errorは0件である。

Issue #15の散歩モードは`PlayerWalkModeController`としてPlayerへ追加・保存済みである。Choice Action `walk` のYES側は`DialogueManager`からこのControllerを開始し、散歩開始時だけ現在の`PlayerAreaTracker.CurrentAreaId`に対応する地点へPlayerを安全に再配置し、LastSafePositionとAreaを更新してから会話UIを抑止、PlayerCameraとそのAudioListener、`PlayerSafetyMovementController`を有効化する。`Desk`ならMainSceneの`PlayerTransitionPoints/PlayerSpawn_Desk`、それ以外は`PlayerSpawn_Table`を使い、Desk地点未設定の旧SceneではTable地点へフォールバックする。通常会話中はこれらを無効にし、通常会話CameraとそのAudioListenerだけを有効にする。散歩開始の遷移中は安全に入力を停止し、完了直後に入力状態を再適用するため、初回から歩行・マウス視点・カーソルロックが有効になる。MainSceneのPlayerには両Start DestinationをInspector保存済みで、未保存時だけ同名パスを探索する互換フォールバックがある。WalkMode中だけ暫定キー`P`で終了でき、終了Areaが`Desk`ならDesk、それ以外はTableを既存`NekolposGameBootstrap.SetHomeLocation`へ渡して、Player位置を変えずに通常会話UIへ復帰する。散歩中に`Esc`で既存ポーズメニューを開くと、同Controllerが`PlayerSafetyMovementController`を停止して歩行・マウス視点回転を止め、カーソルを表示・解放する。閉じると散歩専用UIが未表示の場合だけ入力を復帰する。散歩開始位置とEscポーズ連携を含む`PlayerWalkModeControllerTests`を追加した。Unity Editorが起動中のためTest Runnerは今回実行せず、`dotnet build Assembly-CSharp-Editor.csproj --no-restore`は今回と無関係なUGUIパッケージの`DefaultControls.factory`読み取り専用エラー2件で失敗した。MainSceneのPlayerにはSafety／AreaTracker／Bootstrap／ChatUI／PlayerCamera／Main CameraBのInspector参照を保存済みである。散歩ChoiceのCSV行`01230`は`action_id=walk`へ修正し、`DialoguePreview_Integrated.bytes`を再生成済みである。散歩Action設定の専用CSV回帰テストは成功した。会話CSV回帰全体には、`exercise`の未解決choice分岐と`dlg_01330_go_back_desk_p1_o1_normal`の空Routeという今回と無関係な既存データ不備が2件残る。

Issue #16では`PlayerSafetyMovementController`へ、`Jump Height`（MainSceneはユーザー調整済みの`0.1m`）と暫定キー`Space`による単発ジャンプを追加した。初速は`gravity`と`jumpHeight`から算出し、CharacterController有効・Walkable接地・接地中だけ開始する。空中再ジャンプはできず、上昇速度は`Max Fall Speed`でクランプしない。頭上衝突時は上昇速度を停止する。上昇中の水平移動は、ジャンプ高さと安全な段差範囲にWalkable着地点がある時だけ許可し、着地点のない虚空は既存`EdgeFallPrevention`でブロックする。ジャンプ中は`LastSafePosition`を更新せず、着地後は既存の安定時間判定へ戻る。`PlayerSafetyMovementTests`を含む関連EditMode回帰は48/48成功、Console Errorは0件である。実機で段差越えと端からの飛び出し防止を確認する必要がある。

Issue #17では、散歩中だけPlayerCamera中央から短距離SphereCast（既定距離`0.12m`、半径`0.008m`、LayerMaskはInspector設定、Triggerも対象）を行う`PlayerInteractor`と、`IPlayerInteractable`／`PlayerInteractable`の共通基盤を追加した。E入力は入力欄選択中、散歩外、散歩メニュー表示中には実行しない。`NerukoTalkInteractable`は通常会話へ戻さず、PlayerCameraを維持した散歩専用メニューを開き、メニュー中だけPlayer移動・視点操作を停止する。初期候補の「ここで待ってて」「付いてきて」「運んでほしい」は未実装として無効化し、「何もしない」だけで同じ位置・Areaの散歩へ戻る。`CableTravelInteractable`は既存`PlayerAreaTransitionController.TransitionToAsync`だけを利用して床⇔Desk間を遷移するため、座標を直接変更しない。MainSceneにはPlayerの`PlayerInteractor`、`Normal_Cat`の`NerukoTalkInteractable`＋Trigger Collider、`CableInteract_Floor`と`CableInteract_Desk`のTrigger Collider＋相互Destination・Area Idを保存済みである。対象EditModeテストは14/14成功、Unity Console Errorは0件。仕様とScene設定手順は`docs/PlayerInteraction.md`に記録した。

Issue #18では`PlayerSafetyMovementController`のEdgeFallPreventionに安全な段差降りを追加した。前方の通常接地がない場合でも、既存Walkable Layer・最大SlopeのSphereCastで検出でき、現在の足元より低く、`Maximum Safe Step Down`（MainSceneは`0.03m`）以内の着地点だけ通常歩行を許可する。虚空、同値を超える落差、歩行不能な法線は引き続き停止する。移動はTeleportやSnapを使わず、既存重力による短距離降下のままなので、段差降り中はLastSafePositionを更新せず、下側で安定接地後に既存処理へ復帰する。`PlayerSafetyMovementTests`はEditModeで15/15成功、Unity Console Errorは0件である。

Issue #19では、通常会話と独立した`WalkDialogueController`をMainSceneのPlayerへ追加した。専用データは`Assets/Resources/Dialogue/WalkDialogue.csv`で、`TriggerType, TriggerId, Speaker, PsychologyState, Text, Order`を必須列とする。不正行・空文はWarningを出して読み飛ばし、通常の`DialogueManager`／暗号化TalkDataへ混在させない。ネルコE操作は心理状態ごとの一方通行コメントを表示して散歩専用Choiceを開く。Affection等に文面が未登録の場合はNORMALへフォールバックし、未指示の台詞を生成しない。吹き出しは発話者ヘッダー・状態別アクセント・輪郭・影を持たせ、Player説明は別配色で表示する。発話中はクリック／タップで次行へ進み、無操作時だけ自動送りするが、操作案内テキストは表示しない。`PlayerAreaTracker.AreaChanged`、`WalkDialogueTrigger`、`WalkDialogueObjectInteractable`でArea／家具IDの反応を接続でき、Cableの`CableDesk`コメントは既存の安全遷移前に再生する。散歩終了は`action_walk_result`／`action_walk_result2`／`action_walk_result3`から1件をランダム選択して既存`DialogueManager`の時間経過イベントへ遷移する。完全暗転後、同イベントのcallbackで最寄りTable/Desk Anchorへ`PlayerAreaTransitionController.TeleportImmediately`による安全移動を完了してから通常会話カメラ・UIへ復帰する。時間進行・結果表示・明転・通常会話入力への復帰は既存イベントだけが一度だけ行う。MainSceneのPlayerにはController、Normal_Cat、PlayerCamera、OpenBetaSystemsのDialogueManager／TimeManager／StatusManager、AreaTransitionController、Table/Desk Anchorを保存済み。対象EditModeテストは8/8成功、Console Errorは0件である。設定手順は`docs/WalkDialogue.md`に記録した。

Issue #20では、Issue #8の特別日記機能（永続解禁、月間の肉球、特別日付だけを1日1セルへ並べるソート、特別日付リストの日送り、Detail→Calendarのソート維持、`TEMP_SPECIAL_DIARY_001`）が既に実装・MainSceneへ接続済みであることを再確認した。Debug Start時だけ表示される`NekolposDebugPanel`へ`10/1 特別日記を追加`を追加し、`TEMP_SPECIAL_DIARY_OCT01`を保存済み`GameStartDate`から算出した次の10月1日のDay Numberへ追加する。操作は既存`TimeManager.SetCurrentTime`経路を使い、PC実日時や通常開始時の自動追加には依存しない。同じ機能は`DiaryCalendarController`のInspectorコンテキストメニューからも利用できる。`TabletDisplaySceneRegressionTests`はEditModeで25/25成功、Unity Console Errorは0件である。

日記の通常保存日は`DiaryCalendarController.AddEntry()`が`TimeManager.GetCalendarDate(CurrentDay)`で決める。Debug Startの夜日記では、通常草稿を`WritingFinishButton`で確定保存した直後に`AdvanceToNextMorning()`を実行し、次回Debug Start用のDayを`Nekolpos.Debug.StartDay.v1`へ保存する。確定せず日記を閉じるかPlayを終了した場合は、当日分の朝・昼・夕の通常草稿を破棄し、既存仕様どおり特別日記Pendingは残す。Debug Panelには、Day 1とゲーム内開始日を実PC当日へ戻す「日付リセット」と、保存済み日記・草稿・特別日記解禁を消す「日記リセット」を追加済みである。

改名監査では、旧`OpenBetaTitleBootstrap`型・ファイル参照は現行Assets/ProjectSettings/docsから検出されなかった。`OpenBeta.PlayerName`等のPlayerPrefs互換キーは変更しない。残る`TitleScene`は履歴文書、PV／Tail試験Sceneの実ファイル名、または旧名を冠したEditor支援クラス・ログ・ローカル変数であり、`GameScene`は別Sceneとその照明・編集ツールのパスである。これらはMainSceneへの改名対象か判断できないため、自動改名しない。

ラベルなしの直接指定Issue #12・#13・#14・#17・#18には、変更ファイル・実装内容・テスト結果・ユーザー側確認事項を含む完了報告をそれぞれ投稿済みである。

Issue #11を実装済み。`H:`はBootstrapが登録する`PlayerAreaTracker.CurrentAreaId`を優先して評価する。MainSceneの`NekolposGameBootstrap > Player Area Tracker`には本番PlayerのTrackerを保存済みであり、未割り当て時も型ベースで一度だけ解決して登録する。Trackerが登録されたMainSceneで`None`なら正の`H:`条件は不成立、否定条件は成立し、猫のホーム位置へフォールバックしない。Trackerを持たない旧テストSceneだけは明示的な互換処理として`CatPositionController.CurrentConversationLocation`を使う。猫の基本会話拠点・猫/カメラ配置の責務は変更していない。`ConversationLocationConditionTests`はEditModeで15/15、`PlayerAreaTrackerTests`は2/2成功した。Unity ConsoleのコンパイルErrorは0件である。

`Assets/Scenes/MainScene.unity`はBuild Settingsの先頭Sceneである。Bootstrapは、Meta GUIDを維持して`NekolposGameBootstrap.cs`／`NekolposGameBootstrap`へ改名済みであり、C#型参照、`MainScene`の3件の永続UnityEvent型名、Scene名定数、Pause Menu、Editor用Scene Builder／Release Build Menu／回帰テスト、現在仕様のドキュメントを同時に更新した。`MainScene`は単体ロード・保存済みで、`NekolposGameBootstrap`が1件だけ存在する。テール試験SceneとPV Sceneのシリアライズ表示も新クラス名へ同期した。`TabletDisplaySceneRegressionTests`はEditModeで23/23成功し、Unity ConsoleのErrorは0件である。`PlayerLookTarget.DestroyObject`のメンバー隠蔽Warning 1件は今回変更と無関係な既存項目として残る。

基本会話地点は `CatPositionController` が一元管理し、`Table` / `Desk` / `Bathroom` / `Bed` を保持する。会話Condition `H` はこの状態を参照し、複数位置のORと `!` 否定指定を評価する。通常会話と選択肢は同一の評価経路を使用する。

`MainScene` の `NekolposGameBootstrap > Conversation Timeline Actions` には、出発・到着の通常 `PlayableDirector` を共用する `MoveToDesk` / `MoveToTable` を登録済み。`NekolposGameBootstrap` は `FadeController.BecameOpaque` を待ってから状態・猫・拠点カメラを遷移先へ更新し、待機姿勢・カメラ優先視線Rigの更新を暗転中に完了する。共有Timelineの再生時間は暗転中に待たず、`BecameTransparent` の後に必要なタブレットカメラ補間を開始する。`homeLocationChangeTime` は既存Scene互換用で実行時に参照しない。各移動先ごとのTimeline、Signal Asset、Signal Receiver Reactionは不要である。

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

`TabletHomeApplicationController` は `OpenBetaTitleBootstrap` が実行時に追加するタブレットホーム専用Controllerである。既存のIconParentとPowerIconをStartPanelへまとめ、`NyanstaIcon → NyanstaPanel`、`ShopIcon → ShopPanel`、`demaeIcon → DemaePanel`、`GameIcon → GameCenterPanel` を接続する。同名PanelがSceneに未配置の間は、既存のTabletCanvasアプリ親Panelの子として最小ページとCloseButtonを生成する。DiaryPanelも同じControllerへ登録され、日記のCloseButtonはDiaryPanelをDiaryIconへ縮小してStartPanelを表示する。Sceneを直接変更せず、先行する未保存Scene変更との競合を避けた。

GitHubのOpen `codex-task` Issue #5を外部指示として実装した。`.codex/tools/github_issue_progress.py` は`origin`のリポジトリに限定し、既存の無関係ラベルを残したまま、着手時に`codex-task → codex-working`、完了時に結果コメント投稿後`codex-working → codex-review`、ユーザー確認後にCloseを実行する。完了報告は変更ファイル・実装内容・ビルド/テスト結果・ユーザー確認事項の4見出しを必須とし、認証情報やAPI失敗詳細を出力しない。Issue読取側は「タスク5」の日本語指定も明示Issue番号として解釈する。GitHub CLIのブラウザ認証を完了し、Issue #5を作業結果コメント付きの`codex-review`へ移行した。

GitHubのOpen `codex-task` Issue #6を外部指示として実装した。`AGENTS.md`に、MCP失敗時は再初期化して同一操作を即時・短時間待機後・最終の計3回再試行すること、MCP必須操作が全試行で失敗した場合は未実行のまま完了扱いにせず中断・報告すること、安全で同等な代替を使う場合は未確認範囲を明記することを定義した。GitHub CLIのブラウザ認証を完了し、Issue #6を作業結果コメント付きの`codex-review`へ移行した。

保存済み`StartPanel`がタブレットアプリ共通親を兼ね、`NyanstaPanel` / `ShopPanel` / `DemaePanel`がその子かつ`SizeDelta=10×10`で残る状態を検出した。`TabletHomeApplicationController`は、既存StartPanelがIconParentの直接親である場合にその親をアプリ共通Rootとして解決し、StartPanel・各アプリPanel・DiaryPanelをRoot直下の全画面RectTransformへ正規化する。`TabletApplicationWindowTransition`は中断時に開始前の全画面位置・スケールを復元する。Diaryは内部CalendarView用の別遷移を生成せず、DiaryPanel自体を他アプリと同じ共通遷移で開閉する。保存済みのNyansta/Shop/DemaePanelにCanvasGroupがない場合も、遷移前に必ず追加してMissingComponentExceptionを防ぐ。

GitHubのOpen `codex-task` Issue #3を外部指示として実装した。`DiaryJournalStore` は通常草稿と特別Pendingを確定済み日記とは分離して保存する。時間経過イベントはCSVの `diary_id` / `diary_kind=special` を解釈し、死亡フローは翌朝へ送る前に特別Pendingを追加する。夜へ切り替わった時間経過イベントの完了後、Deskとタブレット閲覧へ遷移する。通常時は日記アプリではないプレイヤーの思考UIで朝・昼・夕方を選び、選択後にのみ `WritingView` を独立ウィンドウとして開く。音声入力風の文字送り後、終了で初めてカレンダーへ確定保存する。`ReWritingButton` は未確定本文を破棄して思考UIへ戻すため、草稿・Pendingを失わない。

会話Action専用の `ConversationMove_Start_Director` / `ConversationMove_End_Director` は Play On Awake をオフにする。これにより起動時にDesk移動Timelineと暗転が自動実行されず、既定の基本会話地点 `Table` を維持する。

`C:\Users\shoho\20260814 Nekolpos.zip` 内の `Nekolpos/Assets/Resources/Dialogue/SystemTimedEvent.csv`（3,017 bytes、アーカイブ内更新日時 2026-05-25 02:37:30 JST）を復元候補として確認した。現行 `TalkSource/TalkCSV/SystemTimedEvent.csv`（1,741 bytes、2026-09-15更新）とは別系統の10キーであり、現行12キーを失わないよう上書きではなくマージが必要である。

`Build/PC`（2026-08-09）の `sharedassets0.assets` から、同梱鍵で復号した `SystemTimedEvent`（5,699 bytes、25キー）を確認した。現行12キーとの重複はなく、8月ビルド版と現行をマージすれば37キーを安全に保持できる。`Build_Release_Diag`（2026-07-23）にも21キー・4,949 bytes版があるが、8月版のほうが4キー多い。

`TalkSource/TalkCSV/SystemTimedEvent.csv` は8月ビルド版25キーと現行12キーをマージして37キーへ復旧し、`Assets/Resources/TalkData/SystemTimedEvent.bytes` も同じ鍵で再暗号化済みである。復号ラウンドトリップにより、実行用bytesとマージ済みCSVの完全一致を確認した。

## 完了したこと

- #71: 自由単語辞書・累積質問/訂正/読み・条件付き挨拶・カテゴリ変数/HasValue・メモ/確定忘却の一通りを接続。軽い学習で時計を進めない。

- #28のMainScene再現・自動Follow開始・距離とArea設定修正、#29のDesk/Table/Conversation復帰、NightDiary/Pause/タイトルCleanupのMainScene実行Smokeを完了した。

- Issue #31の日付保存・保存日のMorning再開、#33の共有CleanupとScene寿命修正、#32のWindows監査・条件コンパイル検証を完了した。

- 直接指定Issue #27・#28・#29の修正、既存テストファイルへの回帰追加、関連仕様書の更新を完了した。新しいC#ファイル・Manager・Scene設定の追加は行っていない。 各Issueへ原因・変更ファイル・104件の検証結果・実機確認事項を含む完了コメントを投稿済み。IssueはOpenのまま保持した。
- 写真撮影用`FreeCameraController`と分離して、本番用`PlayerSafetyMovementController`にCharacterControllerベースの通常歩行、Walkable判定、崖端防止、SafePosition復帰を実装した。
- プレイヤー安全移動の回帰テストと対象SceneへのInspector設定・公開APIを`docs/PlayerSafetyMovement.md`へ追加した。
- `PlayerAreaTracker`と`PlayerAreaVolume`を追加し、現在地の参照、重複領域の決定的な優先順位、未所属、変更イベント、Gizmoを実装した。
- エリア判定の設定・公開APIを`docs/PlayerAreaTracking.md`へ追加した。
- Issue #23の猫用NavMesh追従、Follow/Wait、Area間Warp、Animator・Presentation・LookRig連携、MainScene設定、回帰テスト、`docs/CatLocomotion.md`を追加した。
- Issue #24のホーム非移動Locomotion Animator API、状態公開、回帰テスト、Animation棚卸しを追加した。

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

- #72: Assets/Scripts/System/CommandToneInput.cs、System/Editor/CommandToneInputTests.cs（各meta）、ChatUIController.cs、DialogueEngine.cs、DialogueManager.cs、docs/StatusSystem.md。


- #71: Dialogue/UnknownWordの新Store/Controller/Memoと既存3ファイル、DialogueEngine/Manager/ChatUI/ProgressSaveStore/BasicSystemDialogueCatalog、ConversationVariableResolver/ConditionEvaluator、TabletHomeApplicationController/Bootstrap、WordLearningAuthoring/学習テスト、MainScene/Word系3Prefab、BasicSystemDialogue.csv/対応bytes、docs/LearnedWords.md/SteamCloudSave.md/TimeFlow.md。

- #61: SettingsPanel.cs、SettingsVolumePreviewPointer.cs（新規・meta）、Editor/GameSettingsTests.cs、docs/GameSettings.md。#60: TimeFlowCoordinator.cs（新規・meta）、TimeManager.cs、Editor/TimeFlowCoordinatorTests.cs（新規・meta）、DialogueManager.cs、DiaryCalendarController.cs、ActionManager.cs、ActionExecutor.cs、StateMorning/Day/Evening/Night/Dead.cs、NekolposDebugPanel.cs、docs/TimeFlow.md（新規）/DiaryJournal.md/DebugMode.md。

- #28再調査・Smoke: MainScene.unity、CatLocomotionController.cs、NekomataLookRigController.cs、WalkDialogueController.cs、CatLocomotionControllerTests.cs、docs/CatLocomotion.md、docs/SmokeTest-20261003.md、.ai/HANDOFF.md。#29のカメラ経路は既存実装で正常動作を確認し、新たなカメラ変更は不要だった。

- #31〜#33: TimeManager.cs、NekolposGameBootstrap.cs、OpenBetaPauseMenuController.cs、DiaryCalendarController.cs、TabletHomeApplicationController.cs、TabletApplicationWindowTransition.cs、FadeController.cs、PlayerAreaTransitionController.cs、PlayerWalkModeController.cs、WalkDialogueController.cs。既存WeatherSystemTests、PlayerWalkModeControllerTests、PlayerAreaTransitionControllerTests、TabletDisplaySceneRegressionTestsを更新。docs/DiaryCalendar.mdとdocs/WindowsStandaloneAudit-20261003.mdを更新・作成した。
- MainScene.unityはMCP TestRunnerService.SaveDirtyScenesIfNeededにより、ユーザーの未保存状態が自動保存された。こちらからScene編集・ロード・Build Target切替・Play開始は行っていない。保存前のdirty状態へは戻せないため、その事実をユーザーへ説明した。

- Issue #27〜#29: `Assets/Scripts/Player/Interaction/PlayerInteractor.cs`、`PlayerInteractionPrompt.cs`、`Assets/Scripts/Player/PlayerWalkModeController.cs`、`Assets/Scripts/Camera/PlayerSafetyMovementController.cs`、`Assets/Scripts/System/OpenBetaPauseMenuController.cs`、`NekolposGameBootstrap.cs`、`CatLocomotionController.cs`。
- 回帰: `Assets/Scripts/Player/Editor/PlayerInteractionTests.cs`、`PlayerWalkModeControllerTests.cs`、`Assets/Scripts/System/Editor/CatLocomotionControllerTests.cs`。仕様: `docs/PlayerInteraction.md`、`docs/CatLocomotion.md`、`docs/TabletPowerControl.md`、`docs/PlayerSafetyMovement.md`。
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
- `Assets/Scripts/System/CatMotionController.cs`
- `Assets/Scripts/System/CatLocomotionController.cs`
- `Assets/Scripts/System/Editor/CatMotionControllerTests.cs`
- `Assets/Scripts/System/Editor/CatLocomotionControllerTests.cs`
- `Assets/Scripts/Player/PlayerWalkModeController.cs`
- `Assets/Scripts/Player/WalkDialogue/WalkDialogueController.cs`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/NavMeshAreas.asset`
- `docs/CatLocomotion.md`
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
- `Assets/Scenes/MainScene.unity`
- `OpenBetaCanvas/TabletButton`（MainScene内UI参照）
- `Assets/Timelines/MoveToStart.playable`
- `TalkSource/TalkCSV/DialoguePreview_Integrated.csv`
- `Assets/Resources/TalkData/DialoguePreview_Integrated.bytes`
- `docs/BasicConversationLocations.md`
- `docs/DiaryCalendar.md`
- `docs/DiaryJournal.md`
- `docs/PlayerAreaTracking.md`

## 実施した動作確認・テスト

- #72: Unity MCP正式EditMode88/88成功（新規44、失敗0/Skip0）、Console Error0、MainScene/Play=false/compiling=false/dirty=false。output/command-tone/tests-final.json。


- #71: 正式Runner142/142＋最終UI2/2成功、Skip0。273 Runtime WebGL条件コンパイルError0/Warning264。MainScene Playの登録/再認識/忘却取消・確定/Tablet復帰と学習朝挨拶置換を確認。output/learned-words/tests.xml・tests-final-ui.xml・play-result.json・greeting-play.json・2PNG。Editor保存復元済み。

- #61は19/19、#60最終は64/64、失敗0/Skip0。設定Pause中プレビュー/音量/独立Source、時間経過6入口/夜停止・重複拒否/死亡Pending翌夜/5会話結果後更新・Title取消/3秒暗転・翌朝/天気・異常系を検証。成果はC:/Users/shoho/.codex/tmp/issues61-60。

- 最新Smoke: MainScene実PlayをMCPから既存メソッドと移動APIで操作。Table追従約0.20m/停止距離0.4485m、Wait位置不変、Floor再追従距離0.4491m、接触Wake、Desk復帰72フレーム・Table復帰53フレームを観測。会話固定Camera描画0、Player/Cameraの位置・回転差0、Walk/Input復帰を確認。Conversation復帰もPivot位置/回転差0。夜日記は起動とCloseのみ、確定・日送り・本文生成は行わない。PauseでTimeScale0→Resume1。Tablet/Diary/Fade/Pause途中のGoTitle後は状態解除、Fade透明、Canvas残留0、Bootstrap1個。修正後fresh Play/停止のConsole Error0件。
- 最新回帰EditMode4/4成功（距離/Area契約、Rig再読込、Choice接続、Navigation契約）。Playへ入る2つを含む最初のMCPジョブはDomain Reload後にテスト開始前で孤立し、clear_stuckで解除した。2つの自動UnityTestは完走未確認のため成功扱いにしない。代わりに上記MainScene実Playで振る舞いを確認した。Windows/WebGL条件コンパイルも最終変更でError0件、既存Warning3件。人の実入力とWindows実行ファイルは未検証。

- #31〜#33: Unity MCP接続成功、EditMode15/15成功。最後の修正後はSceneを保存しないexecute_code経由でNUnitメソッド17件をSetUp/TearDown・Unity LogScopeとともに実行し全件成功。検証途中の直接呼び出しはLogScope不足／通常ログも失敗にする不適切な検証関数で失敗したため、Test Runnerと同じEvaluateLogScopeを用いて再検証した。実装不具合ではなかった。保存日数は各Weatherテストで退避・復元。結果はプロジェクト外C:/Users/shoho/.codex/tmp/nek313332/final-tests.json。
- Windows/WebGL Player条件をUnity同梱Roslynで個別コンパイルしエラー0件。既存警告3件（PlayerLookTargetの名前隠蔽、WalkDialogueの旧WordWrapping、未使用walkEndMinutes）は監査資料に記録。Windows Player Build・実機起動は未実施。最終Unity Console Error0件、今回変更C#のdiff --checkは成功。全体diff --checkには既存TextEditPreviewState.asset:32の空白が残る。

- Issue #27〜#29: Unity 6000.3.10f1のネイティブバッチで関連104/104成功、失敗・スキップ0件。CatLocomotion20、CatMotion4、Timeline19、Bootstrap遷移5、Tablet画面27、TabletWake5、Interaction6、WalkMode18件。Play Modeへ入る実行テストで、Follow開始・停止・Wait・休止復帰、タブレットの散歩復帰と会話復帰を検証した。元のFollow停止距離・休止復帰処理では2件とも移動量0で失敗し、修正後は0.76m離れたPlayerへ約0.31m移動して約0.45mで停止した。最終ログでコンパイルError・NullReferenceException・MissingReferenceException・Unhandled logは検出されなかった。
- Unity MCP診断は、対象再選択・同一操作の再接続3回でもEditor未接続により未実行だった。安全な代替としてインストール済みUnity Editorのバッチを使用した。描画なしのため、MainSceneでの見た目・実機入力・カメラ境界の最終確認は未実施。結果はローカル作業用保存先の`final.xml`／`final.log`で確認した。
- Issue #23実装後にUnity Asset Refresh・再コンパイルを実行し、Console Error 0件を確認した。`CatLocomotionControllerTests`、`PlayerWalkModeControllerTests`とArea遷移・Interactor・WalkDialogue・Timeline関連回帰は56/56成功した。MainSceneを実物で開く回帰へBinary Scene依存テストを更新し、追加のScene/UI/安全移動回帰47/47も成功した。追加の近距離休止仕様後は`CatLocomotionControllerTests`、`CatMotionControllerTests`、`PlayerWalkModeControllerTests`、Timeline／Bootstrap回帰を31/31成功し、Console Error 0件を確認した。Start／Sleep／End遷移とStart StateのClip参照を追加検証した後は同じ関連回帰を32/32成功し、Console Error 0件を確認した。
- Issue #24実装後にUnity Asset Refresh・再コンパイルを実行し、Console Error 0件を確認した。`CatMotionControllerTests`と`CatLocomotionControllerTests`は13/13、`DialogueTimelineSequenceTests`、`NekolposGameBootstrapPlayerTransitionTests`、`PlayerWalkModeControllerTests`は14/14成功した。
- 猫用NavMeshをUnity APIで実測し、Table／Desk／Floorの全Anchorが`NavMesh.SamplePosition`に成功し、それぞれ`PlayerSpawn_Table`／`PlayerSpawn_Desk`／`CableInteract_Floor`まで`PathComplete`・2 cornersであることを確認した。
- EditMode全体の初回実行は520/528成功だった。8件のうちBinary SceneをYAMLとして読む既存テスト5件、非Active UI配下の除外判定1件、列ずれしたCSVテストfixture 1件を修正し、それぞれの再実行成功を確認した。残る既存失敗は`DialoguePreview_Integrated.csv`の`exercise`グループが`choice_yes_pattern=chase`を参照する一方、同グループの`chase`行が存在しない1件である。キャラクター台詞・会話分岐の自動生成禁止に従い、CSV内容は変更していない。

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

- #72: 指定専用命令会話を追加し実CSVテスト成功。限定辞書外の命令は未検出となる場合あり。実Play/Build/モバイル未実施。


- #71: WebGL実Player Build/ブラウザーIME、PC Player再起動、iOS/Android実機、解像度別UIと最終カメラ演出はオーナー確認が残る。永続化は注入ファイルの再読込で検証済み。関連自動テストに未解消失敗なし。

- #61の実機聴感/マウス・タッチ最終操作、#60の実機日記/就寝/死亡後日記の操作と演出確認。今回の実Player Build・モバイル実機は未実施。関連自動テストに未解決失敗なし。

- #50回帰の残る既存CSV不整合はID 01280 / exerciseのchoice_yes_pattern=chaseの参照先欠落。今回のユーザー修正指示で既存Action直通に解消済み（下記13/13成功）。00490/00491のRegex括弧不整合は修正済み。最終127件中126成功/1失敗、Skipなし。

- 最新Follow/Tablet SmokeはAPIによる既存経路操作とフレーム観測であり、人のマウス/タッチによる身体の見た目・視点操作は最終確認が残る。散歩開始のFollowは近距離では停止するため、Table上で猫から0.55m以上離れて確認する。

- #31/#33: 夕方・夜からタイトル復帰して保存日の朝になること、散歩Choice・Diary記入・Tabletアプリ遷移・Fade途中のタイトル復帰、再入場時の重複UI／入力をMainScene実プレイで確認する。確定日記と特別Pending保持も確認する。
- #32: Windows Development BuildでIME変換／候補位置／確定Enterと送信Enter／Esc／Alt+Tab／DPI／複数Monitor／Quitと再起動／外部リンク／アセット収録を確認する。16:9固定とrunInBackground=1、IME取消EscとPauseの優先、WebGLからWindowsへセーブ移行の必要性はユーザー判断。

- Issue #27〜#29はMainScene実機で、操作案内表示中のPause→タイトルと再開、Followの開始・停止・休止後再開・Wait、Desk/Tableからのタブレット終了時に開始直前の位置・向きへ直接戻ること、会話・夜の日記の既存導線を確認する必要がある。Unity MCPでのConsole確認と描画ありの目視確認は未実施であり、バッチ104/104成功と区別する。
- `MainScene` のOpenBetaCanvasで全ウィンドウが残る直接原因は、`NekolposGameBootstrap.InitializeTitleSession()` 中の `ConfigurePawWakeSources()` が、破棄済みUnityコンポーネントを `??` で保持して `SphereCollider.isTrigger` へアクセスし、`MissingComponentException` でAwakeを中断していたことだった。`TabletWakeSource`と`SphereCollider`はUnityの`== null`を使って個別に再取得／追加し、追加できない足だけWarningでスキップするよう修正済み。足Colliderなしの猫にTrigger ColliderとWake Sourceを作成するEditMode回帰テストも追加した。UnitySkills RESTは8090〜8100の全ポートへ即時・短時間待機後・最終の3回再接続しても到達できず、起動中Unity Editorも変更後の再コンパイルを開始していない。そのためUnityコンパイル、対象EditModeテスト、Playでの単一ウィンドウ表示は未確認であり、Editor復旧後に確認する必要がある。外部`dotnet build`はUnity UGUIパッケージの既存`DefaultControls.factory`読み取り専用エラー、および生成済み参照DLL不足のため検証に使用できなかった。
- `MainScene` の常時の遠景ぼかしは、`NekolposDepthOfFieldController` が `Main CameraB`（Post Processing有効）と、`Assets/Scenes/GameScene/Global Volume Profile.asset` を参照するGlobal Volumeへ毎フレーム書き込むURP Gaussian Depth of Fieldである。通常値はStart `1.45m`、End `6.5m`、Strength `0.45`（実行時Radius約`0.95`）、初期ModeはDefault・有効である。`NekolposURP Asset_Renderer`のRenderer Featureは空で、独自のBlur Render Passはない。`Assets/Scenes/MainScene/Global Volume Profile.asset`のBloomは別効果であり、DoFは含まない。Open Betaの時間帯Volumeは優先度20だがColor Adjustments等のみでDoFを書き換えない。
- `MainScene/TabletDisplay/DisplayArea` は、Edit Modeでは7つの通常Materialだが、Play ModeでFree版タブレットアセット由来の`NotInServiceFreebie (Instance)`が8枠目へ実行時追加される。独立した`EmissionPanel  `（`MAT_TabletEmission`）を無効化してもこのMaterial追加の原因は解消しないうえ、`NekolposGameBootstrap`はタブレット電源ON時にEmissionPanelを再有効化する。ライセンス条件を確認し、許可された有償版・別モデル・自作モデルへ置換して解消する必要がある。Consoleには今回未変更のMissing Script Warningが1件ある。
- `EmissionPanel  `はMesh Rendererに`MAT_TabletEmission`を持つ独立Quadであり、Point Light（intensity `0.07`）と`UniversalAdditionalLightData`を持つ。後者はURPのLight設定であってMaterial設定ではない。Materialは`_EMISSION`有効だがEmissionColorは最大1の通常レンジで、MainSceneが使うGlobal Volume ProfileにはBloomがないため、画面自体の発光感は生じない。画面発光にはMaterialのHDR Emission強度とBloomを設定する必要がある。周囲を照らしたい場合だけ別途Point Lightを調整する。
- `VFX_Room_Dust_01`はPlayerの子にある補助Particle Systemで、Rendererは無効・`None`のまま保持する。実描画は子`Dust Specs`が担当し、Local Simulation Space、Billboard、Cast Shadows／Motion Vectorsオフ、Sorting Fudge `0.1`である。親`room_dust`はPlayer基準で上方3 cm・前方20 cmに置き、`Dust Specs`はユーザー調整済みScale（`0.04, 0.03, 0.04`）を保持する。旧`Vefects`カスタムシェーダーがURP上で粒を描画しなかったため、`Assets/Materials/M_PlayerDustSpecs_Additive.mat`（URP Particles/Unlit・加算）を割り当てた。Play Modeで発生・描画を確認済みで、Prewarm有効、Emission Rateは控えめな`4`である。
- OP中の遠方猫注視について、`OPAnime`という名前のGameObject／Timelineは存在しない。現行の実体は`OP_Cat B/Arm_Cat`のPlayableDirector（`Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Anim/OBOP_MoveStart.playable`、11.483秒）で、`NekolposGameBootstrap`のPattern B `Move Start Director`として参照される。Cinemachineは未導入で、`Main CameraB`の親`CameraEventPivot`が開始・OP後のカメラ姿勢をBootstrapから直接受ける。Timelineには既に`CameraEventPivot`へバインドされた`Animation Track (2)`があり、5.867秒以降のPivot姿勢と終了直前の復帰を駆動している。遠方猫注視を実装する際は、この既存Trackを拡張して6.8秒以降の注視区間と終了姿勢を編集し、同じPivotへ別のAnimation Trackを重ねない。`OP_Cat B`には`neck/head`ボーンがある。DoFはOP猫がActiveの間、`NekolposDepthOfFieldController`が毎フレームOffにし、通常猫への切替後に同ControllerがGaussianへ復帰する。現時点では調査のみでScene・Timelineの変更はしていない。
- OP中の遠方猫注視について、`OPAnime`という名前のGameObject／Timelineは存在しない。現行の実体は`OP_Cat B/Arm_Cat`のPlayableDirector（`Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Anim/OBOP_MoveStart.playable`、11.483秒）で、`NekolposGameBootstrap`のPattern B `Move Start Director`として参照される。Cinemachineは未導入で、`Main CameraB`の親`CameraEventPivot`が開始・OP後のカメラ姿勢をBootstrapから直接受ける。Timelineには既に`CameraEventPivot`へバインドされた`Animation Track (2)`があり、5.867秒以降のPivot姿勢と終了直前の復帰を駆動している。`OpeningCatLookAtController`はMoveStart開始時に`Arm_Cat`へ実行時追加され、0〜5.83秒だけTimeline評価後のPivot回転へ平滑化した`head`注視を上乗せする。5.55〜5.83秒で重みを0へ戻すため、既存の突き飛ばし・上向き・CameraShakeに干渉しない。FOVは48から65へ4.70〜5.70秒で復帰する。DoFは同Controllerから既存`NekolposDepthOfFieldController.BeginOpeningFocus(head)`へ通知し、0〜5.83秒だけカメラからheadまでの距離＋`0.15m`をGaussian開始距離、そこから`1.5m`を終了距離として追従する（強度`0.45`、補間`0.2秒`）。解除後は従来どおりOP猫がActiveな間はOff、NormalCat切替後は通常設定へ戻る。設定はBootstrapの`Opening Cat Look At`とDoF Controllerの`Opening Focus`で調整し、任意の`Opening Cat Focus Target`が未設定なら`head` Boneを使う。`Animation Track (1)`の無限Clip `Recorded`が`OP_Cat B`へ0〜4.633秒に書き込んでいたRotX `-90°`は全3キーを`0°`へ修正済みであり、`Arm_Cat`のユーザー設定`-90°`と二重にならない。`root_bone`の実RotationはIdentityで、約90°の`m_LocalEulerAnglesHint`は実行時に適用されないEditor表示ヒントのため変更しない。仕様は`docs/OpeningCatLookAt.md`に記録した。

- #9の実機確認は完了しているが、現在のPlayer一式は`MainScene`に保存され、GameSceneには存在しない。本番対象をGameSceneにする場合は、明示的にPlayer・Walkable床・AreaVolumeを配置または移動し、#9の安全移動を再確認する必要がある。対象Sceneが未確定のため自動移動はしていない。
- 本番Playerへ`PlayerAreaTracker`を追加し、必要な場所に`PlayerAreaVolume`を配置して、Area Id・Priority・Resolve Orderを設定したうえで、実プレイ中の出入り・重複領域の優先順位を確認する必要がある。Scene自動編集はしていない。

- Unity Editor上で実プレイし、Choice承認時に暗転、完全暗転イベント後のDesk移動、`H: Desk` 会話への切替を確認する必要がある。
- Unity Editor上で実プレイし、`MoveToDesk` / `MoveToTable` で完全暗転後にだけ猫の配置・向き・姿勢・視線が更新され、明転後に猫が表示されてから必要時のタブレットカメラ補間が始まることを確認する必要がある。
- Unity Editor上で実プレイし、MoveToDesk後のタブレットカメラ補間、カーソル解除、および `ExitTabletCameraView()` による復帰を確認する必要がある。
- Unity Editor上で、Desk・通常入力待ちだけTabletButtonが表示され、押下中・閲覧中は隠れ、復帰後に再表示されることを確認する必要がある。
- Unity Editor上で、DiaryIconから日記を開き、月送り制限、日付セルのプレビュー／本文、`AddEntry(string)` の保存・再表示を確認する必要がある。
- Unity Editor上で、Diary DetailViewの `9/21の日記` 形式のタイトルと、前後の日記ボタンの遷移・端での無効化を確認する必要がある。
- Unity Editor上で、Issue #7のCalendar→Detail、Calendar→Writing、Writing/Detail→Calendarの左右スライドと、`DetailBackImage`だけが動く日送り、遷移中の入力ロック、および画面外へページが描画されないことを実プレイ確認する必要がある。
- Unity Editor上で、特別日記取得直後に肉球付きソートボタンが解禁され、通常月間表示の肉球、特別ソートの1日1セル、特別日付だけの日送り、Detail→Calendarでのソート状態維持を実プレイ確認する必要がある。
- Unity EditorをDebug Startで起動し、`10/1 特別日記を追加`後に9月からNextで10月へ移動して10/1セルの本文プレビュー・肉球、特別ソート、Detailの前後日送りを実機確認する必要がある。
- Unity Editor上で、Nyansta・Shop・demae・GameCenterがアイコン位置から全画面へ拡大し、Diaryを開閉した後も各アプリPanelが全画面の正しい座標で開くことを確認する必要がある。`GameIcon → GameCenterPanel`を含む`TabletDisplaySceneRegressionTests`はEditModeで27/27成功したが、画面遷移の実機確認は残る。
- Unity Editor上で、Issue #25の消灯中にPlayerが画面へ乗る／`CatSimple_Lie_belly_loop_3`中の前足、または後ろ足が画面へ触れることで点灯すること、点灯後だけ`E：操作する`が表示され、アプリ表示を維持して操作へ入ること、離脱後の8秒消灯、散歩復帰を実機確認する必要がある。
- Unity Editor上で、Issue #17の散歩開始後にネルコ反応点の`E：話す`、散歩専用メニュー、床⇔Deskの電源コード往復、`何もしない`の位置・Area維持を実機確認する必要がある。反応点は既存のPlayerSpawn Transformに子として置いているため、電源コードの見た目に合わせた最終的なCollider位置・大きさの調整も必要である。
- Unity Editor上で、Issue #23の散歩開始後に「付いてきて」でTable・Desk・Floorを追従し、近距離で停止してPlayerを押し潰さないこと、「ここで待ってて」でArea移動後も元のAreaへ残ること、散歩終了・通常会話・Timeline・Openingで移動所有権が競合しないことを実プレイ確認する必要がある。
- 01280/exerciseの欠落chaseは今回修正済み。既存action_gameoftag_play_resultへ直接接続し正規CSV回帰は成功、実Playでの追いかけ演出確認は未実施。
- Unity Editor上で、Issue #18のティッシュボックスからちゃぶ台側・ベッド側へ前進だけで安全に降りられることと、側面・大きな落差では止まることを実機確認する必要がある。
- Unity MCPブリッジを再接続後、`UiInteractionRegressionTests.ApplyTree_LeavesTabletButtonOutsideAutomaticButtonConversion` を実行し、Console Error 0件と合わせて確認する必要がある。
- 現在のPlay Mode Consoleには、今回未変更の`NekomataLookRigController.cs:1255`（`RigBuilder.Build()`）でのNullReferenceExceptionが1件ある。タブレット関連のConsole Errorは0件だが、別タスクでRig参照の初期化状態を確認する必要がある。
- Unity Editor上で、夕方から夜へ進む時間経過イベント後のDesk・タブレット遷移、通常草稿の選択・音声入力風の文字送り、書き直し後の草稿保持、終了による保存、死亡を含む複数Pendingの発生順記入と翌日以降の保持を手動確認する必要がある。Debug Startでは通常日記を確定した瞬間に翌朝へ進み、次回Debug StartもそのDayから始まること、未確定で閉じる／Play終了した当日草稿だけが消え特別Pendingは残ること、Debug Panelの「日付リセット」「日記リセット」を確認する必要がある。
- Tableへ戻す会話を作成する場合は、CSVのActionを `play_conversation_timeline:MoveToTable` にするだけでよい。Timeline・Director・Signalの追加は不要。

## 今回確定した仕様・判断

- #72: 命令口調判定は原文の独立コピー/限定辞書、既存会話マッチング後に送信ごと最大従順+10。専用効果と別枠、再評価/再生は非加算。StatusManagerの履歴/代表心理/保存/減衰と既存5会話結果を使用。


- #71: 自由登録とTeachingDiscoveryは別辞書/キー。未定義場面では場面指定挨拶を自発抽選に含めない。その他の全体抽選重み1/一般学習4/基本1。教示知識にゲーム効果なし。固定UIはPrefab、可変一覧だけ行PrefabをInstantiate。タブレットは既存Bitmap TMPフォントを共有する。

- #60: 全進行は共通TimeFlowへ依頼、長時間要求はNightで止め残り時間を破棄。固有後続Patternも日記/翌朝へ持ち越さず破棄する。同時要求は拒否しキュー化しない。死亡/就寝の翌朝更新と、新規/保存朝復元/開発絶対日時・巻き戻しを例外として保持。夜Stateは同期のみ、日記は共通側1回。

- ユーザー追加指示により、散歩開始は従来WaitからFollowへ変更。Wait/Follow Choiceは維持する。Tableで到達可能な開始距離0.55m、停止0.45mへ調整する。見た目・台詞・Scene水平範囲は変更しない。

- 日付だけの最小保存で、時刻・UI・会話・散歩状態は保存しない。既存キーと既存TimeManagerを共有し、新ManagerやC#ファイルは追加しない。Windows監査はコードと条件コンパイルの確認範囲を明示し、native実機IMEの合格を推測しない。

- プレイヤー安全移動はRigidbodyへの移行や現在地Condition統合を行わず、写真撮影用`FreeCameraController`と別の`PlayerSafetyMovementController`として実装する。Walkable Layer名はコードへ固定せず、InspectorのLayerMaskで指定する。
- 現在地判定は安全歩行から分離し、Trigger通知を補助として使いつつAreaVolumeの毎フレーム再評価を正本とする。Dialogue Condition、ケーブル移動、ネルコ搭乗・追従は接続しない。

- 場所移動の現在地更新とStart→End連結はコードが担い、Signal Receiverは共通の暗転・明転用途に限定する。
- 位置ConditionはTransform検索や毎フレーム距離判定を行わず、`CatPositionController.CurrentConversationLocation` の状態のみを参照する。
- タブレット閲覧カメラの姿勢はScene上の `TabletCameraPoint` を正本とし、場所移動の明転完了後にだけ起動する。
- ChatGPT側で外部作業指示用のGitHub Issueを作成する際は、`codex-task` ラベルを必須とする。ラベルなしIssueはCodexの自動作業指示として取得されない。
