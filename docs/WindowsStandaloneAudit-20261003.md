# Windows Standalone監査（Issue #32）

対象は現行MainScene・Runtime C#・Plugins・PlayerSettings・EditorBuildSettings。2026-10-03時点。C#をUnity同梱Roslynと既存Player参照でWindows/WebGLの両条件に分けてコンパイルした。出力はプロジェクト外。Build Target変更、実行ファイル作成、Windows実機起動は行っていないため、これは条件分岐とC#参照の検証である。

|区分|対象|判定・対応|
|---|---|---|
|A 問題なし（コード確認）|WebGLImeInputBridge / NekolposWebGLIme.jslib|`__Internal`呼出しとDOM制御はUNITY_WEBGL && !UNITY_EDITOR内。WindowsはUnity InputField経路。JSライブラリはPlugins/WebGLにありネイティブDLLは追加されていない。|
|A|OpenBetaCharacterSetupPanel / OpenBetaCallCatPanel / Bootstrap|WebGL専用入力橋は条件分岐で除外。Windowsは既存InputFieldと送信Buttonを利用。|
|A|ChatUIController / ImeCompositionVisualController|nativeのInput.compositionStringを参照。変換中・確定直後の送信抑止、Enter以外のフォーカス解除で送信しない判定、候補位置更新あり。操作感はCで検証。|
|A|Quit / 外部リンク|PauseとTitleのQuitはWindowsでApplication.Quit。WebGLは終了Buttonを非表示。Steam/アンケートはApplication.OpenURLで既定ブラウザへ。|
|A|日数・日記保存 / DialogueLogFileExporter / RuntimePerformanceDiagnostics|PlayerPrefsとpersistentDataPathを利用し、Assetsフォルダへ書き込まない。EditorでのpersistentDataPathはLocalLow/CatLover/Nekolpos。Windows PlayerPrefsとWebGLの保存は自動共有されない。|
|A|Build Scenes|有効SceneはAssets/Scenes/MainScene.unity。場所移動のScene経路はCanStreamedLevelBeLoadedで未登録を拒否。Windows向けに追加必須と断定できる別Sceneは見つからなかった。|
|B 修正済み|日付再開 / タイトル復帰|#31でCurrentDay保存を通常プレイへ統一。#33で散歩会話Canvasの寿命、日記・タブレット・フェード・移動中断・入力を解除。Windowsにも適用される共通処理。#32固有の不具合修正は不要だった。|
|C 手動確認|日本語IME|Microsoft IMEで漢字変換、確定Enter→送信Enter、候補表示位置、Esc取消、クリック送信、Alt+Tab前後を実行ファイルで確認。native候補Windowの見た目・誤送信をコードだけで合格扱いしない。|
|C|ウィンドウ / FixedWindowResolution|UNITY_STANDALONE_WIN && !UNITY_EDITOR内のuser32呼出しはWindows条件でコンパイル成功。16:9維持・最小960×540・ウィンドウ位置復元あり。DPI100/150/200%、複数Monitor、最小化・復帰、全画面切替を確認。|
|C|Focus / Pause / Quit / 再起動|runInBackground=1、resizableWindow=1、activeInputHandler=0。Alt+Tab時の入力・音声、EscでPause、終了と再起動後の保存日Morning、確定日記、外部リンクの動作を確認。|
|C|Build / Assets|C#条件コンパイルは成功したが、Windows PlayerのBuild・Shader・アセット収録・IL2CPP/Mono実行・音声は未検証。MainSceneの見た目も手動確認。|
|D ユーザー判断|PC固有の既存挙動|ウィンドウ16:9固定、バックグラウンド中の進行、EscでIME取消とPauseの優先、WebGLからWindowsへのセーブ移行が必要かは仕様判断。現状を独断で変更しない。|

検証結果: Windows/WebGLのRuntime C#条件コンパイルはエラー0件。共通の既存警告はPlayerLookTarget.DestroyObjectの名前隠蔽、WalkDialogueControllerの旧enableWordWrappingと未使用walkEndMinutesの3件。実機の不具合を示すものではなく、ここでは挙動変更を伴う修正はしていない。

次にWindows Development Buildを作成し、Cの確認を行う。Steam SDK・クラウド保存・UI再設計は今回対象外。

## 対応箇所と根拠の補足

|区分|ファイル・メソッド|リスクと推奨対応|
|---|---|---|
|A|Assets/Scripts/System/WebGLImeInputBridge.cs: Refresh / Hide / ForceHideDomInput / SetSuspended、Assets/Plugins/WebGLのjslib|DOMと__InternalはWebGL Player条件に限定。Windowsではnative InputFieldを使い、DOM側へ転送しない。|
|C|Assets/Scripts/System/ChatUIController.cs: OnEndEditSubmit、ImeCompositionVisualController.cs: Refresh / UpdateCompositionCursorPosition|IME確定と送信、候補位置、フォーカス復帰はnative実機入力を確認する。|
|A/C|Assets/Scripts/System/FixedWindowResolution.cs: ApplyAspectRatio / RestoreWindowPosition / OnApplicationFocus|user32はWindows Player条件のみ。DPI・複数画面・最小化・Fullscreenを手動確認し、16:9仕様自体はDの判断対象。|
|A|Assets/Scripts/System/DialogueLogFileExporter.cs: EnsureWriter / ReplaceWriter / Flush、Assets/Scripts/TimeSystem/TimeManager.cs: SaveCurrentDay / LoadCurrentDay|persistentDataPath/PlayerPrefsを正とし、ログはPath APIで構築。WindowsとWebGLの保存先の自動移行はない。|
|B|Assets/Scripts/System/OpenBetaPauseMenuController.cs: GoTitle / ResetOpenBetaState、NekolposGameBootstrap.cs: StartNormalGame / PrepareSavedMorningState|日数再開と一時状態破棄は共通経路。タイトルロード前に解除するため、再入場時の前Session UIを引き継がない。|
|A/C|OpenBetaPauseMenuController.cs: QuitGame / 外部アンケート経路、Assets/Scripts/Editor/ReleaseDiagBuildMenu.cs: BuildReleaseDiag|QuitはApplication.Quit、リンクはApplication.OpenURL。実行ファイル・ブラウザ・ビルド収録の最終確認範囲は結果に従って更新する。|

追加検証として、未保存MainSceneを別名コピーしたSceneと一時的なDevelopment Player検証コードを使い、Windows Playerで日数保存→終了→別プロセス起動を確認する。元Sceneの自動保存を避けるためMCP BuildRunnerのSaveDirtyScenesは使用せず、Unity BuildPipelineへ検証コピーを明示する。結果が出るまでWindows実行の合格とは扱わない。一時コードとSceneは検証終了後に削除する。

2026-10-03 最終結果: Windows実Player再起動検証完了。Development BuildはSucceeded、errors=0、warnings=39（約5時間8分）。PID27704でDay3/Night・Walk/Tablet/Pause中の保存と終了、別PID9528でDay3/Morning（同一日付2026-10-05）を確認。復帰時Walk/Tablet/PlayerCamera/Prompt=false、Fade透明、TimeScale=1、Cursor=None/visible。Diaryが同一TimeManagerを参照する検証も成功。native-save.json/native-resume.jsonは両方passed=true、両ログにException/Error/failedの一致なし。-batchmode -nographicsによる検証なので描画・IME実入力は未検証。native日数/開始日/Diary関連Prefsは事前退避へ復元し一致確認。一時Assets/__CodexGoalValidationはAssetDatabaseで削除済み。元MainScene保存操作なし（現在dirty=false）。検証コード/Sceneコピー・exe・ログはC:/Users/shoho/.codex/tmp/goal-auditへ保持。元BuildTargetのWebGLへ復元済み、building=false・compiling=false・一時AssetsなしをMCPで確認。

## 会話中のTAB非表示の暫定停止（2026-10-04）

ChatUIController.UpdateからTABによるToggleWindowVisibility呼び出しを撤去。会話中のTABでウィンドウを非表示にしない。非表示ボタンと既存の自動表示制御・クリックによる復帰は維持する。一部UIだけ消える根本的な表示整合の改修はユーザー指示により後日対応。写真撮影モードのTAB操作は対象外。
