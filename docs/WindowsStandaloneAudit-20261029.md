# Windows Standalone 互換性監査（2026-10-29〜30 / Issue #32）

現行の Runtime C#、Plugins、MainScene、Build 設定を監査し、Windows から unityroom ランキング送信を呼ぶ経路を修正した。旧 [2026-10-03 の監査](WindowsStandaloneAudit-20261003.md) は履歴として保持する。

監査・実ビルドの対象は未コミット変更を含むローカル checkout。PR は既定ブランチ `chore/ai-handoff-foundation` の `930b6073` に Reporter の5行追加、外部検証ツール、監査文書、HANDOFF だけを重ねる。監査対象の Scene、フォント、保存基盤、SE、ホッケー等をこの PR に含めた意味ではない。PR 単体のソース条件コンパイルとローカル全体の Build を区別する。

分類は **A: 現行コード上でプラットフォーム境界を確認、B: 今回修正した具体的な不具合、C: 実 Player / 手動確認が必要、D: ユーザーの仕様判断が必要**。A は実機合格や全通信・全書込の遮断を意味しない。

## 結論と旧文書の訂正

- 今回の B は `UnityRoomTeachingRankingReporter.SubmitScore` の送信対象制限。元実装では Windows からも unityroom package の相対 API 送信へ進み得る。現 checkout に WebGL Player 限定と隔離拒否の小変更が反映されていることを確認した。後述の条件別検証は担当者の結果と併記する。
- 旧文書の「#32 固有の修正不要」は現在の Reporter には適用できない。
- 旧文書の「Windows は 16:9 固定・最小 960×540」は現実装の主経路を表さない。`FixedWindowResolution.ApplyAspectRatio` は Windows Player で SettingsManager があれば戻り、SettingsManager の表示設定を優先する。これらの定数は fallback 側に残る。
- 旧文書の保存説明は現 Windows の通常進捗ファイルへ更新が必要。Windows Player の通常進捗は `persistentDataPath/Saves/progress-v1.json`、初回には旧 Prefs 移行があり得る。隔離起動を通常保存の実機検証に数えない。
- 2026-10-03 の Development Build と headless 二プロセス再開の成功は過去版の証拠。現在の Scene / 保存基盤 / 表示設定 / Reporter の合格へ外挿しない。旧文書にある通常 Prefs の退避・復元方式を今回の実行手順として再利用しない。

## A: 現行コード上で確認した境界

| 対象 | ファイル・メソッドと根拠 | 判定の範囲 |
| --- | --- | --- |
| WebGL IME / DOM | `Assets/Scripts/System/WebGLImeInputBridge.cs:32` の extern 群、`Refresh:157`、`Hide:233`、`ForceHideDomInput:244`、`SetSuspended:255`、`SyncText:269`。DOM / `__Internal` 呼出しは `UNITY_WEBGL && !UNITY_EDITOR`。 | Windows は native InputField 経路。Windows で DOM を使う前提はない。 |
| タイトル入力 / 会話 IME | `OpenBetaCharacterSetupPanel` と `OpenBetaCallCatPanel` の WebGL 分岐。`Assets/Scripts/System/ChatUIController.cs:1908` の OnEndEditSubmit → TrySubmit:1962、TrackImeComposition:2320 / IsImeComposingOrJustCommitted:2328。変換中と確定直後、Enter 以外の focus 解除による送信を抑制する。`ImeCompositionVisualController.cs:55` の Refresh は WebGL を除外し、UpdateCompositionCursorPosition:121 から native 候補位置を更新する。 | コード上の抑止を確認。Microsoft IME の候補 Window と確定操作は C。 |
| Plugin / ネイティブ境界 | Assets 全域の ignore を含めたファイル列挙で `.jslib` は `NekolposWebGLIme` / `NekolposPetting` / `NyanstagramFetch` / `NyanstagramCache` の 4 件、`.dll/.so/.dylib/.bundle/.aar/.a` は見つからない。Runtime の DllImport は IME、Petting、Nyanstagram の `__Internal` と FixedWindowResolution の user32。前者は WebGL Player、後者は Windows Player 条件。 | Assets 内の該当形式・C# 入口の確認。Unity/package 自体のすべての native binary の不存在を主張しない。 |
| WebGL 専用補正 | `WebGLNightClosetAxisStabilizer` の Bootstrap は WebGL Player 条件でだけ自動生成する。MainScene への同 class の直接配置は見つからない。 | 現自動入口は Windows に波及しない。将来手動配置しても無条件で安全という判定ではない。 |
| Windows 表示設定 | `Assets/Scripts/System/SettingsManager.cs:38` の SupportsDisplaySettings、Awake:58、ShouldToggleShortcut:97、ApplyDisplay / coroutine:160–191。Windowed / FullScreenWindow / ExclusiveFullScreen を扱い、入力欄 focus / IME 変換中の Alt+Enter を抑止。`FixedWindowResolution.cs:145` の ApplyAspectRatio は SettingsManager を優先。 | 旧 16:9 固定記述を訂正。通常設定の読取と Screen.SetResolution は隔離時も起こるため CLI windowed の維持を保証しない。 |
| user32 | `FixedWindowResolution.cs:111` の UpdateFullscreenWindowOrder、RestoreWindowPosition:207、extern:266。Windows Player だけで GetActiveWindow / GetWindowRect / SetWindowPos。focus と fullscreen に応じて topmost を設定・解除し、移動時に no-size / no-activate 等を使う。 | 条件分岐の確認。DPI、複数 Monitor、全画面からの復帰は C。 |
| Quit / OpenURL | `NekolposGameBootstrap.cs:3362` の ShouldHideQuitButton と BindPanels:3371 は Windows に終了ボタンを表示して Application.Quit を接続。`OpenBetaPauseMenuController.cs:631` の QuitGame は timeScale を戻して Quit。外部リンクは同:553–562、Bootstrap:3391 / 3401、LogWindowPanel:1010–1018 で Application.OpenURL。 | Windows 向け API 入口を確認。今回のタイトルからの Quit のみ確認済み。既定ブラウザと focus 復帰は C。リンク先へのアクセスは本監査では実行していない。 |
| Resources / 暗号化会話 | `Assets/Scripts/Conversation/Data/ConversationDataManager.cs` の Resources.Load 系と ResolvePackagedCryptoSetting:905、ResolvePackagedCryptoSettingFromResource:959。同梱 Resource を先に読み、必要時には StreamingAssets の key file を Path.Combine / File API で読む。 | 会話・Walk・GameCenter・フォント・Mixer・SE の Resource ファイル存在を確認。復号・Build 収録・実ロード成功は C。秘密内容は本書に記載しない。 |
| StreamingAssets / SNS | `LocalNyanstagramPhotoSource.cs:15` は HTTP StreamingAssets の読取を拒否し、Native は Path → file URI で catalog / image を読む。`NyanstagramTransport.cs:49` の非 WebGL GET は timeout / size / redirect 制約を持つ。RemoteCachedNyanstagramPhotoSource.CanConnect:34 は同意と設定を要求する。NyanstagramView.CacheRoot:168–175 は隔離時に DebugFileRoot を使用する。 | Windows の file URI 経路を確認。隔離は SNS 全通信拒否ではなく、同意後の送信は可能。実ネットワークは未実行。 |
| Scene / Build scene list | `ProjectSettings/EditorBuildSettings.asset` は有効 Scene が `Assets/Scenes/MainScene.unity` のみ。Windows Build Profile は global scene list を継承。`NekolposGameBootstrap.LoadLocationScene:2824` は空名と CanStreamedLevelBeLoaded=false を拒否してから LoadScene。現 MainScene の locationTransitionSceneName は空。 | 今回 Windows 用に新 Scene を加える必然性は見つからない。MainScene の実表示と参照の動的解決は C。 |
| 保存 / 診断出力 | `ProgressSaveStore.cs:99–120` の native file / migration、159–219 の隔離 memory と Save 拒否。WorkerPlayerPrefs、GameSettings.Save、会話ログ出力・upload、RuntimePerformanceDiagnostics、WindowsScreenCaptureController、PhotoStudio に隔離拒否あり。 | 通常 adapter と実 Player の初期化・終了順序は未検証。隔離起動の制約は本書の検証記録を参照。 |
| スクリーンショット | `WindowsScreenCaptureController.cs:39` の Update、CaptureScreenshot:111、CaptureFrame:119、SavePng:140。Windows 条件、DebugSaveSession.IsIsolated 拒否、通常 persistent/Screenshots 配下で CreateNew。 | 通常撮影の実レンダリング・HUD・書込権限は C。隔離実行で撮影しない。 |

## B: 今回の Reporter 修正

元の `Assets/Scripts/System/UnityRoomTeachingRankingReporter.cs` は Start → SubmitCurrentScores と discovery 更新から共通 SubmitScore に入り、プラットフォームや隔離を検査せず Reflection で UnityroomApiClient.SendScore を呼んでいた。

`Library/PackageCache/com.unityroom.client@e4454788006b/Api/Internals/Scoreboard.cs` の SendScoreEnumerator は Editor のみを除外し（48 行付近）、Native では UnityWebRequest.SendWebRequest へ進む（59–60）。CreateRequest の URL は `/gameplay_api/v1/scoreboards/.../scores` の相対 URL（90）。package asmdef は include / exclude platforms の制限がない。このため「WebGL 専用の package だから Windows では無害」とは扱えない。

最小修正は `SubmitScore:65–82` の本体を `UNITY_WEBGL && !UNITY_EDITOR` 内に限定し、その中でも `DebugSaveSession.IsIsolated` なら return するもの。通常 WebGL の board 判定と既存送信処理を保ち、Windows / Editor / 隔離起動の送信を抑える。新しい Steam SDK や別送信基盤は追加しない。DebugSaveSession.IsIsolated は PR #80 より前の base にも存在するため、#80 の新しい入力 API を必要としない。

現 checkout の Reporter SHA-256 は `4B3C056EDBC707EA6DCF998707B40532326BE4F5F3AE7E22F47143DDBA1293CE`。今回の Build はこの修正を含む source を明示条件とする。再現用ツールは `.codex/tools/unityroom_reporter_checks/`。fake Unity/package の条件別テストを実 Unityroom 通信の検証と混同しない。

## C: 実機 / 手動確認が残る項目

| 項目 | 確認する振る舞い・残る限界 |
| --- | --- |
| IME / 入力 | Microsoft IME の漢字変換、候補位置、確定 Enter と送信 Enter の分離、Esc 取消、クリック送信、Alt+Tab の前後。source だけで合格にしない。 |
| 表示設定 / focus | Windowed / FullScreenWindow / ExclusiveFullScreen、任意比率の許可、DPI 100 / 150 / 200%、複数 Monitor、最小化復帰、Alt+Enter、topmost 解除。通常設定を読む起動は CLI 指定を後から上書きし得る。 |
| Pause / Quit / 外部リンク | Windows の既存終了ボタンによる正常終了、timeScale / cursor、外部ブラウザ後の focus、背景中入力・音声。通常保存による再開は別の安全確認が必要。 |
| 保存 | Windows の正常 file 書込・旧 Prefs 移行・再起動ロードは実 adapter 未検証。今回のゲーム保存変更0という観測を、通常保存・移行・再起動機能の成功に読み替えない。Unity 本体の Screenmanager 等の Registry 書込もゲーム側の隔離ガード外。 |
| Build / assets | 今回の BuildReport、target / Development、Shader / フォント / 会話復号 / StreamingAssets / Resource / 音声 / 描画。今回のタイトル表示以外の実表示とロードは未検証。SoapBubble 専用 Shader の欠落警告と代替表示を下記に記録。 |
| 通信 | 同意 UI の状態と Reporter の対象制限はコード確認。Unity / OS / 第三者を含む通信ゼロの実測は未検証。手動で SNS 同意・更新・いいね・外部リンクを実行しない限定起動とは範囲が異なる。 |

## D: 仕様判断を保持する項目

- 背景で進行する仕様（PlayerSettings.runInBackground=1）と focus 外の音声。技術監査で独断変更しない。
- Esc の IME 取消と Pause / 設定閉じる操作の優先。OpenBetaPauseMenuController.Update:93 と SettingsPanel の Escape 経路には IME composition の明示判定がない。実機挙動を確認し、必要な優先仕様を決めてから変更する。
- 既定 FullScreenWindow、任意比率の表示設定、topmost などの PC 向け体験。旧「16:9 固定」を復活させる根拠にはしない。
- WebGL から Windows へのセーブ移行、Steam SDK / cloud save、UI 再設計。今回の互換性修正とは独立した仕様。

## 今回の検証記録

- Reporter 全文を fake Unity / unityroom に接続した外部テストは Windows Release / Development、WebGL Editor / Release / Development の各18件、計90件成功。旧コードへの負の対照は Native / Editor 各14件、WebGL 各5件の送信漏れを検出した。通常保存・実通信・Unity assembly を実行しない。
- remote base の Runtime 210ソースに Reporter だけを重ね、Windows / WebGL の Release / Development 条件で外部コンパイル成功。警告は base と同数（Release 149、Development 204）。最初の WebGL 条件検証は Windows 用参照一覧に WebGLModule がないため base / 変更後とも同じ1エラー。インストール済み WebGLModule を参照して再実行し成功した。旧失敗ログも保持した。
- 既存 Unity 6000.3.10f1 への反映後、Console Error 0、コンパイル失敗なし。Editor 内の Reporter.SubmitScore の実ロード済み IL は `ret` のみ。
- Windows64 / Mono / Development Build は成功。エラー0、警告1、96.54秒、2,404,363,995 bytes。MainScene はユーザー保存済みの `7ad98840…` を指定。自動 Scene 保存・AssetDatabase.SaveAssets・AutoRun・別 Unity 起動は行っていない。既存 Build callback の副作用は下記へ区別する。
- 警告1件は「未コンパイルの変更があり、post-process/import の変更が反映されない可能性」という Unity 警告。生成 DLL / PDB をロード・実行せず読み、Reporter の送信無し、Development 隔離引数処理、WorkerPrefs の隔離分岐5経路を確認。PE / PDB 対応一致、Runtime 269文書・Editor 152文書の source checksum 不一致0、Build 後に取得した live Editor callback の MVID も一致した。警告の発生原因自体は未確定であり、原因を一時 Import と断定しない。
- SecurityBuildGuard の暗号設定有効・機密開発入力0を開始前に確認。一時 `Assets/Resources/BuildSecrets` と同 meta は開始前・終了後とも不在、CleanupPending=false。値や秘密情報は記録・公開しない。

### ビルド前後の保全

通常 persistent フォルダー1232項目、native PlayerPrefs 40項目×32/64 view、Editor PlayerPrefs 130項目×32/64 view、global EditorPrefs 863項目×32/64 view、ProjectSettings 32項目、Packages 776項目は差分0。

Assets は7456項目中、次の3フォント Asset に差分があった。Scene / C# とその他 Assets は差分0。**全 Assets 保全は不合格のまま保持する。** 値の復元や差分の一括除外は行わない。

- `Assets/Fonts/NotoSansSC-Regular SDF.asset`
- `Assets/Resources/Fonts/PlayerInputZenMaruGothicDynamic SDF.asset`
- `Assets/Resources/Fonts/TabletNotoSansJPBoldDynamicBitmap.asset`

3件は Dynamic かつ clearDynamicDataOnBuild=true で、TMP の既存ビルド前処理による glyph / character table と atlas cache のクリアと整合する。Git 管理外で、ビルド直前 bytes と一致する退避は見つかっていない。古い別 snapshot を上書き復元に使わない。独立レビューでは、この生成キャッシュ差分の完全な元 bytes 復元を Native 保存隔離の依存にはせず、現在状態を追加変更の検出用 baseline として限定起動する判断を記録した。元 Asset 保全の不合格とは区別する。

### 限定 Player 起動と未検証範囲

今回適用した起動制約は、今回の Development Build に完全一致 `--nekolpos-isolated-debug` と output 配下の `-logFile` を指定し、タイトル表示→既存終了ボタンだけに限定するというもの。Release ではこの隔離フラグは無効。GameSettings の通常値の読取と表示適用は隔離時も行われ、CLI の Windowed 指定を上書きし得る。

通常保存 adapter / 再起動ロード、SNS 許可・更新・いいね、外部リンク、設定保存、PhotoStudio、debugExport は実行対象外。特に DebugConsoleTrace.ExportLocal は隔離時も通常 persistent フォルダーへ書く。OS・ドライバー・Unity 全体の完全 sandbox ではなく、Screenmanager 等の native Registry 書込も監査対象にする。

日本語 IME、描画・聴感、DPI・複数画面・Fullscreen・Alt+Tab、通常保存と移行、実ネットワークは未検証。正式 NUnit / 旧 fixture / worker は起動していない。#80 の Editor 内シナリオ成功を Windows Player の成功へ読み替えない。

### 実 Player の今回結果

今回生成した exe を隔離フラグ付きで1回起動した。タイトルの日本語表示を確認し、既存の「ゲーム終了」ボタンで終了、プロセス消滅とログの Input Shutdown / Cleanup を確認した。会話開始、設定変更、SNS、外部リンク、保存・ロード、debugExport は操作していない。OS の終了コード自体は取得していない。

起動前 baseline は postbuild と全項目一致。終了後は通常保存ファイル、Assets、ProjectSettings、Packages、Editor PlayerPrefs、global EditorPrefs が不変。native PlayerPrefs に Unity セッション4キー・表示8キーの変更を観測した。32/64 view が同じため24差分記録は12個の名前に対応する。ゲーム固有の保存キー変更は検出しなかった。

全12名を hash で対応付け、名前の基部が今回の UnityPlayer.dll に存在することも確認した。ただし native setter の callsite を計測したわけではない。表示8キーは次回起動にも影響し得る実永続変更であり、無除外の **Native 全体保全判定 false は維持する**。除外・値の復元・2回目の起動は行っていない。

Player ログの Error / Exception 一致は0、警告は1件。`TalkTopicHintBubble.GetBubbleMaterial` で `Nekolpos/UI/SoapBubble` が見つからず、既存の軽量 sprite 表示へ fallback した。shader source は存在するが、収録・stripping の原因までは確定していない。タイトル以外の実表示と Shader の収録を追加確認し、見た目に関係する変更を検証せず混ぜない。

CLI では1280×720 Windowedを指定したが、実際の取得画面は約1920×1080のタイトルバー付き表示だった。通常設定の適用・ウィンドウ操作等の影響を分離していないため、CLI指定どおりやFullscreen合格とは判定しない。IME / Alt+Tab / DPI / 音声 / 実保存は引き続き C。

### 再現用ツールと記録

```text
python .codex/tools/audit_tool_checks.py
python .codex/tools/unityroom_reporter_checks/verify.py
python .codex/tools/windows_state_audit.py snapshot --out output/windows-audit-before.json
python .codex/tools/windows_state_audit.py snapshot --out output/windows-audit-after.json
python .codex/tools/windows_state_audit.py compare --before output/windows-audit-before.json --after output/windows-audit-after.json --out output/windows-audit-diff.json
```

Reporter 検証は Python 3.10+ / .NET SDK 10、保存監査は Windows / Python 標準ライブラリーだけを使う。保存監査は読取のみで、名前・値を hash 化し、アクセス失敗・読取中に検出した変更・reparse point を拒否する。無除外・無復元、既存結果を上書きしない。ファイル時刻・ACL・外側の OS cache は比較対象外。監査ツール自体の合成42件も成功。必須10group・identity・exemptions・entryの破損入力を拒否し、比較CLIの終了値0/1/2と既存結果の上書き拒否を確認した。Reporter検証器も開始時にincomplete manifestを記録し、SDK・旧ソース・compileの途中失敗で前回の成功が残らないことを確認した。

最終検証器での再実行は `output/issue32-20261029/reporter-final-checks/verification-manifest.json` に記録し、90/90・completed=true・passed=true・対象ソース不変を確認した。合成42/42は `output/issue32-20261029/audit-tool-final-checks.json`。各ツールと結果の SHA-256 は証跡JSONに含める。

機械可読の要約は [証跡](WindowsStandaloneAudit-20261029.evidence.json)。詳細のローカル成果物は `output/issue32-20261029/`。実行ファイル・秘密 Resource・通常保存・レジストリー値は PR に含めない。

## 依存と競合

Reporter の安全修正は remote base に既存の DebugSaveSession.IsIsolated を使い、PR #96 の新 API に依存しない。一方、ローカルの隔離 Build 検証は #80 の保存保護を含む現 checkout を使う。この二つを混同しない。

#93 / #80 はそれぞれ PR #95 / #96 へ分離済み。#81 は別チャットの調査 PR #97 と照合し、SE、FootContact、ホッケーを重複編集しない。#38 / #61 の過去テストを現在版の実聴感や未接続四足の検証へ流用しない。共有 HANDOFF だけは各 PR の要約を保持して統合する必要がある。既定ブランチへの merge と Issue Close は行わない。
