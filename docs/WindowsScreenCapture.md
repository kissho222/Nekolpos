# Windows HUD表示切替・スクリーンショット（Issue #44）

Windows版ではF10でスクリーン空間のHUD表示を切り替え、PrintScreenで描画完了後のゲーム画面をPNG保存する。既存のLegacy Inputに合わせる。NekolposGameBootstrapがWindowsScreenCaptureControllerを接続する。Windows Editorでも検証可能、他プラットフォームでは接続・キー処理を行わない。

HUDはルートCanvas以下の枝にCanvasGroupを利用し、描画時だけalphaを0へ変更する。GameObjectのActive、Selectable、EventSystemの選択、CanvasGroupのinteractable/blocksRaycasts、会話・入力・時刻・プレイヤー操作は変更しない。次のUI更新前には元のalphaへ戻し、通常UI制御による最新の値を描画時に保持する。非表示中に生成された選択肢・ヒント・案内Canvasも対象とし、ignoreParentGroupsを使うPopupも非表示にする。固定UIの生成や位置・フォントの変更は行わない。

FadeController.OverlayRoot以下と、InspectorのPresentation Roots以下は除外する。ロード表示・シーン遷移表示を追加する際はPresentation Rootsに登録する。現行の暗転はActionFadeOverlayで、FadeとHUDが同じCanvasでも暗転を維持する。ワールド空間のタブレット画面は世界の表示として維持する。シーンロード・Active Scene変更・Controller無効化時は表示へ戻す。旧ChatUIControllerのTAB切替は撤去済み。

PrintScreenはHUD表示を変更せず、WaitForEndOfFrame後にCaptureScreenshotAsTextureで実描画サイズを取得する。保存先はApplication.persistentDataPath/Screenshots。フォルダーを作成し、日時（ミリ秒）とGUID付きNekolpos_*.pngをCreateNewで書く。成功時はConsoleへ保存先、失敗時は明確なErrorを出力する。撮影通知UIは未追加。PhotoStudioの専用カメラ・固定解像度撮影は別用途のため維持する。

現在のWindows保存先は `C:/Users/shoho/AppData/LocalLow/CatLover/Nekolpos/Screenshots/`。ユーザー名部分は実行ユーザーに依存する。

## 検証（2026-10-05 JST）

正規Unity Test Runner EditModeは4/4成功・失敗0・Skip0（2026-10-04 16:05:50Z〜16:05:52Z）。HUDのActive/Input設定と最新alphaの復帰、ignoreParentGroups Popup、新規HUD、Fade/World Canvas除外、破棄済み対象、Scene変更時の復帰、PNG作成・連続保存・空データ/書込先不正を確認。最初の4件中1件失敗はEditModeでOnDisableが自動実行されない検証方法を修正して再実行した。

MainScene Playでタイトルと会話画面のHUDあり/なしPNGを保存し、実画像で消失を確認。710×399のPNGはScreen.width/heightと一致。会話画面の切替前後でIsInDialogueMode/IsTyping、入力Active/Interactable、EventSystem選択、TimeScaleの一致と復帰を確認。Windows Release Buildは25.07秒・1963.45MB・Error0/Warning0。成果は `C:/Users/shoho/.codex/tmp/issue44-20261005/`、実行ファイルは `Nekolpos 0.0.6/Nekolpos.exe`。最終Play停止・compiling/building=false・Scene dirty=false・Console Error0、Scene保存操作なし。

Windows Playerでの物理F10/PrintScreen、OS側PrintScreenとの併用、実シーン遷移、通常入力待ち/散歩での実操作はユーザー確認が残る。今回の実PNG検証はWindows Editorの公開制御入口を利用し、物理キー入力の検証とは扱わない。MCPはDomain Reloadで切断したが再接続で回復、Runner結果XMLを証跡として退避した。
