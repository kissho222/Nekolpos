# MainScene Smoke Test（2026-10-03）

対象: Issue #28 Follow、#29 Tablet復帰、関連するPause・夜日記・タイトルCleanup。
Unity 6000.3.10f1／Unity MCP／MainSceneの実際のPlay Modeで検証した。既存のStart/Choice/Interact/Exit/GoTitleメソッドを呼び、Player位置は移動APIで設定した。人がマウス・タッチで操作した目視テストやWindows実行ファイルのテストではない。

## 原因と修正

- Followの初期状態がWaitだった。ユーザーの追加指示により散歩開始時をFollowへ変更。Waitから退出して再び散歩する場合もFollowで始める。
- MainSceneの開始距離0.75mに対し、Tableの開始位置からNavMeshで到達できる最遠点は約0.665mだった。Table内では開始条件へ届かないため、既定値・MainSceneを0.55mへ変更。停止0.45m、目的地オフセット0.4m、ヒステリシスは維持。
- Table/Deskの判定Boxは立位CharacterController中心を含まずArea=Noneになる。上端だけ8cm拡張し、下端と水平範囲は維持。
- Scene退出時、破棄済みPresentation参照を?.が通してMissingReferenceExceptionになる。Unityのnull比較に統一。
- Script再読込後、WalkDialogue Catalogが欠落するためOnEnableで再取得。RigLayerはisInitialized=trueだけ復元されconstraintsが失われる場合があり、そのLayerを同じRig/activeを持つ未初期化Layerへ置換してからBuildする。例外の抑制は行わない。

## 実行結果

|項目|結果|
|---|---|
|散歩開始|Choiceを選ばずFollow=true、Agent有効・NavMesh上、Area=Table|
|Table追従|距離0.6486mから猫が約0.2001m移動し、距離0.4485mで停止。停止後は既存Sleepへ。|
|Wait|PlayerをFloorへ移しても猫位置(-0.3186,0.4065,-1.4508)を維持。|
|Follow再開・Floor|Follow Choice経路でFloorへ追従。Player(-1,0.055,-0.3)に対し猫(-0.6493,0.05,-0.5806)、距離0.4491mで停止。|
|Desk Tablet|Playerを実際の画面Triggerへ配置しWake/接触登録を確認。TabletWorldInteractable経路で開始。復帰の72フレームで会話固定Cameraを描画せず、Player位置/回転・PlayerCamera位置/回転の差はすべて0。Walk=true、入力有効、Tabletブロック解除、Area=Desk。|
|Table Tablet|同じ操作メソッド経路へAPIから入り、53フレームを観測。会話固定Cameraを挟まず、4つの姿勢差は0。Walk/入力再開、Area=Table。デバイスはDeskにあるためTableから物理接触したという検証ではない。|
|Conversation Tablet|既存のConversation入口・終了でPivot位置/回転の差0、Walk=false、Tablet表示終了。|
|夜日記|既存BeginNightDiaryでDesk/Conversation Tabletへ接続。Closeで未確定状態を解除。本文の生成・確定や日送りは行わず、既存保存日記・草稿・特別Pendingを消さない。|
|Pause/Resume|PauseでTimeScale=0、Cursor=None。ResumeでTimeScale=1。|
|タイトルCleanup|Tablet/Diary/Fade/Pause途中に実際のGoTitleを呼び、MainScene再ロード後はWalk/Tablet/Transition=false、TimeScale=1、Cursor=None、Fade透明、WalkDialogueCanvas/PromptCanvas残留0、Bootstrap1個。|
|Console|修正後のfresh Playから上記導線の終了・Play停止までError0件。|

## 回帰検証と制約

EditMode Test Runnerは新規の距離/Area契約とRig再読込、既存MainScene接続/Navigation契約の4/4成功（失敗・スキップ0）。最初の6件実行はEnterPlayModeでDomain Reload後にMCP Jobが孤立し、テスト開始前に進行停止した。公式clear_stuckで解除し、Playへ切り替えない4件へ分けて再実行した。2つの自動UnityTestはそのジョブでは完走していないため成功扱いにせず、上表のMainScene実行で移動・停止・待機・休止後の復帰を確認した。

Scene編集前の未保存状態をSaveScene(saveAsCopy=true)でプロジェクト外のC:/Users/shoho/.codex/tmp/nek2829smoke/MainScene-before.unityへ退避し、dirty状態を保持した。その後、対象3設定（開始距離、Table/Desk Box）をUndo対応で編集し、既存のユーザー変更を保持してMainSceneへ保存した。修正後コピーも同じ場所に保存してある。

最後にWindows/WebGL条件でRuntime C#をコンパイルし、エラー0件。3つの既存警告はWindows監査資料の内容を維持する。

人の入力による見た目・身体反応・視点操作の最終確認は残る。自動Followでも停止距離内では動かないので、Table上で猫から0.55m以上離れて確認する。

ユーザーの姿勢指定により、Follow近距離休止をCatSimple_Lie_belly_loop_1へ訂正。コード既定値・MainScene保存値・仕様書・テストを同期した。上表のSleep観測は訂正前の結果である。訂正後はScene設定・Animator Motion・RuntimeソースのEditMode3件成功、Console Error 0件を確認。姿勢変更後のPlay目視は未確認。

## Followアニメーション追加修正

待機loop_1→belly_end→Walk_F_IP→belly_start→loop_1をMainScene PlayのAPI操作で確認した。実Animator Clipはそれぞれ一致し、Start/EndはnormalizedTime約0.85〜0.88で次Stateへのブレンド開始。立ち上がり中の座標は静止、歩行時に約0.20m移動し、近距離で伏せへ戻る。Wait中の座標差0。旧2.5秒休止遅延を廃止し、待機の伏せ状態を散歩開始時から管理する。空の歩行Blend Treeは既存FBX実Clipへ置換・保存した（Controllerは既存Git除外Assets/DL Assets内）。

最終コードのソース/Animator契約2件とBlend時間境界5ケースをEditorで直接呼び出し、7ケース成功。未保存Scene保持のためTest Runnerは使っていない。Play後の見た目はユーザー確認対象。Sceneは開始前の未保存状態を保持し、自動保存していない。

今回のConsoleにはMarker(Clone)のDistanceTextUIでTextMeshProUGUI参照未設定Error1件がある。Follow外の未解決事項として記録し、Error0とは報告しない。

## 散歩視線・猫のTablet復帰追加修正

MainScene PlayのAPI操作で検証。散歩カメラへ注視先を切り替え、両目と首を分離した最終版でTableのPlayerCamera/生成Aim差約0.00000028m、Floor差約0.00000018m。両目のAim軸とCamera方向の角度差はTable/Floorとも0度。首は別ターゲットで既存の角度制限を維持する。Floorの視線測定ではPlayerSafetyMovementを一時無効にして低いPlayerCamera位置Y約0.10mを固定したため、床の通常操作・接地確認とは別の視線検証である。Table視点Y約0.48m。

Floor/Waitで電源ボタン終了の74フレームを監視し、猫の位置・回転差0、WalkとWait維持。最終版ではTable/Followの歩行を検出してからタブレットを開始し、電源ボタン終了まで187フレーム、猫の位置・回転差0、Walk=true、Follow=true、PlayerCamera有効。通常会話入口でも電源ボタン終了後にHome=Desk、Walk=false、Tablet View/Transition=falseを確認した。

Editor直接実行の回帰4ケース（首の距離/角度境界3ケース、TabletのPlayer/Camera姿勢＋同期通知1件）は全成功。Test Runnerは未保存Sceneを自動保存するため使っていない。最新コンパイル/Console Error 0件、Play停止済み。未保存Scene変更は保持し、今回Scene設定は書き換えていない。操作の見た目はユーザー確認対象。

## #31/#33完了条件の追加照合

現行MainSceneでAPI操作による3回のタイトル復帰を実施。Day2/Night/Walk/Tablet/PauseからGoTitle、通常StartでDay2/Morningへ再開。Day3/Night/夜Diary/PauseからのGoTitleも通常StartでDay3/Morning。日付は共通TimeManagerの開始日＋日数。3回目はCableの既存PromptをSetCurrentInteractableで表示し、2秒FadeToBlack中にPause→GoTitle。復帰後はInteraction対象null、Walk/Diary/Tablet/Prompt=false、Fade透明、TimeScale1、Cursor None/visible、Walk/Prompt Canvas0。

2回のScene再生成後にAssembly-CSharpのstatic Delegateを調査し、破棄済みUnity ObjectをTargetに持つ購読は0。TeachingDiscoveryStore.DiscoveryChanged、WaypointTarget.OnTargetEnabled/OnTargetDisabledは各1購読。ユーザーの日数・開始日Prefsは検証前の状態へ復元した。生ログはC:/Users/shoho/.codex/tmp/goal-audit/GoalAuditDay2Result.txt、GoalAuditDay3Result.txt、GoalAuditPromptResult.txt。ここまでの検証はEditor Playであり、Windowsプロセス再起動とは区別する。

Windowsプロセス再起動の追加検証用にAssets/__CodexGoalValidationへMainSceneの未保存内容コピーとGoalValidationPlayerを一時作成した。通常Start、Day3/NightからWalk/Tablet/Pause中にQuit、別プロセスでDay3/MorningとUI/入力正常化、Diaryの同一TimeManager参照を検証するためのDevelopment Player専用コード。検証後に一時Assetsを除去し、native日数・開始日Prefsも検証前へ戻す。現在はBuildPipelineのビルド結果待ち。

Windows再起動検証の進捗: NativeProbe.rspで現行Runtimeと一時GoalValidationPlayerをWindows条件コンパイルし、Error0、既存Warning3を確認した。これは実Player起動の結果ではない。BuildPipelineはまだ結果未取得・exeなし、Unity PID10624生存。MCPの再初期化後もEditor状態照会は未取得で、二重ビルドやScene保存は行っていない。native実行前にはDay/開始日のProbe内部バックアップに加え、native Diary永続化キーを外側で退避・復元する必要がある。

2026-10-03 追加状態: 同じビルド結果未取得・exeなし・MCP状態照会不能が3連続Goalターンで継続したため、Windows実プロセス再起動検証はblocked。今回も新しいinstance選択による3回の再試行で応答を取得できなかった。Unityプロセスは生存。ビルド終端を確認できないため、一時Assetsは保持し、強制終了・二重ビルド・元Scene保存は行っていない。Unity側のビルド処理とMCP復旧が必要。

2026-10-03 最終結果: Windows実Player再起動検証完了。Development BuildはSucceeded、errors=0、warnings=39（約5時間8分）。PID27704でDay3/Night・Walk/Tablet/Pause中の保存と終了、別PID9528でDay3/Morning（同一日付2026-10-05）を確認。復帰時Walk/Tablet/PlayerCamera/Prompt=false、Fade透明、TimeScale=1、Cursor=None/visible。Diaryが同一TimeManagerを参照する検証も成功。native-save.json/native-resume.jsonは両方passed=true、両ログにException/Error/failedの一致なし。-batchmode -nographicsによる検証なので描画・IME実入力は未検証。native日数/開始日/Diary関連Prefsは事前退避へ復元し一致確認。一時Assets/__CodexGoalValidationはAssetDatabaseで削除済み。元MainScene保存操作なし（現在dirty=false）。検証コード/Sceneコピー・exe・ログはC:/Users/shoho/.codex/tmp/goal-auditへ保持。元BuildTargetのWebGLへ復元済み、building=false・compiling=false・一時AssetsなしをMCPで確認。
