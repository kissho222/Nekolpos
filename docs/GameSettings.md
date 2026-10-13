# 共通設定画面（Issue #38）

表示モード・解像度の共通ドロップダウンは、項目の上端をContentの上端へ揃え、TMP_Dropdownが算出する上下余白をゼロにする。短いリストの最終行が半分見切れることを防ぎ、長い解像度リストは縦スクロールで選択する。

## 構成と入口

現行Build対象はMainSceneのみ。タイトルはOpenBetaIntroPopupPanel、通常プレイのPauseはOpenBetaPauseMenuControllerが担当するため、別のTitleScene/GameSceneは新設しない。両者の「設定」ボタンから同じSettingsPanelを開く。固定UIはAssets/Prefab/SettingsPanel.prefabをMainSceneへ配置し、両入口のSerializeFieldから同じSceneインスタンスを参照する。既存のZenMaruGothicフォントとUIStyleを使用する。

GameSettings.csは設定データ・検証・PlayerPrefs保存、SettingsManager.csはDontDestroyOnLoadの設定適用正本、SettingsPanel.csはScene単位の共通UGUI画面の状態・イベント・値反映を担当し、UI生成・レイアウト・フォントの上書きをしない。SettingsManagerはBeforeSceneLoadで生成し、シーン再読込でも保持する。SettingsPanelはモーダル背景とスクロール可能な画面/音量/操作の各セクション、戻るボタンを持つ。設定値は変更時に即時反映・保存する。

Pause中に設定を開いてもTime.timeScale、PlayerWalkModeControllerのPause通知、ChatUIの入力禁止、IME停止を解除しない。戻る/EscはSettingsPanelだけを閉じ、Pauseメニューへ戻る。同一フレームのEscがPause解除まで伝播しない。FPSCamera/FreeCameraController/PlayerSafetyMovementControllerはPause中と設定画面中に操作を処理しない。

## 表示と操作

Windows Standaloneのみ表示モード/解像度を変更できる。Editor/WebGL/iOS/Androidでは表示操作を無効化し、Windows用処理は実行しない。

| 表示名 | Unity FullScreenMode |
| --- | --- |
| ウィンドウ | Windowed |
| フルスクリーン | ExclusiveFullScreen |
| ボーダーレスフルスクリーン | FullScreenWindow |

Alt+Enter（左右Alt/Return/KeypadEnter対応）は設定画面外でも共通Managerが処理する。FullscreenからWindowedへ、Windowedから直前のFullscreen種別へ戻る。履歴なしはFullScreenWindow。押下エッジだけで切替、表示変更待機中の再切替は抑制、InputField/TMP_InputFieldのフォーカス中とIME合成中は抑制する。UnityのallowFullscreenSwitch=0を維持し二重切替を避ける。

Screen.resolutionsの幅/高さを重複排除し、現在の安全な解像度を含める。保存済み未対応寸法は起動環境の解像度へ戻す。同一寸法のRefreshRateは現在環境に最も近いものを使う。Screen.SetResolutionの非同期反映を最大120フレーム確認し、確認できなければ警告を出して実際の現在値へ設定とUIを戻す。

初回はデスクトップ解像度のFullScreenWindow。保存後は選択したWindowed/Exclusive/Borderlessと寸法を尊重する。FixedWindowResolutionは従来のフォーカス中TOPMOST/フォーカス喪失解除を維持し、Windowsの起動表示要求はSettingsManagerへ移す。明示選択した4:3等を16:9へ上書きしないため、共通Managerが存在するWindowsでは旧比率補正を停止する。

マウス感度は0.1〜5倍、初期1倍。各Camera/PlayerのInspector感度へ乗算するため初期操作感を保持する。上下反転は初期OFF。VSync初期値は現在のQualitySettings、ONは起動時vSyncCount（最低1）、OFFは0。WebGLではVSync UIを無効化する。

## 音声経路

Assets/Resources/Audio/GameSettings.mixerにMaster直下のBGM/SE/CatVoiceを作成する。公開parameterはMasterVolume/BgmVolume/SeVolume/CatVoiceVolume。0〜1を20*log10(value)へ変換、0は-80dB。100%は0dB。AudioSource.volumeは既存の演出・フェード・距離減衰として維持し、設定音量を重ね書きしない。

- BackgroundMusicControllerの生成Source → BGM。
- CatFootstepAudioControllerの足音 → SE。
- CatPurrAudioControllerのゴロゴロ音 → CatVoice。
- 現在のTimeline AudioTrack（足音、衝突、動作音） → SE。既存Source bindingを維持、未bindingの出力には実行時SE Sourceを割り当てる。
- Scene内の未routing SourceはScene読込/Startおよび毎秒の補助探索でSEへ接続。明示済みGroupは上書きしない。

現行コード・Scene・Timeline調査では台詞ボイス用Source/Trackは存在しない。将来の猫ボイス生成箇所は最初のPlay前にSettingsManager.RouteAudio(source, SettingsAudioCategory.CatVoice)を呼ぶ。既存の足音を猫ボイスへ分類しない。キャラクター台詞・音声を新規生成しない。

旧PauseのBGM/SEボタン参照、Toggle処理、Source.muteの定期更新を撤去。旧BGMButton/SEButtonはEditor移行時にSceneから削除し、設定ボタン・スタック脱出・閉じるボタンをSceneへ配置する。Runtimeではボタン生成や削除をしない。

## 音量プレビュー（Issue #61）

SettingsPanelが専用2D AudioSourceをSE/CatVoiceの既存Mixerへ接続する。InspectorのSE Preview Clip/Cat Voice Preview Clipが未指定なら既存のResources/SE/stepとgoroを使用する。音量は既存Sliderの即時保存・Mixer反映を共有し、AudioSource.volumeへ二重乗算しない。Pause中にも動くようListener Pauseを無視する。

SEは押下開始・操作中の値変更で再生し、unscaledTimeによる0.3秒（Inspectorで0.2〜0.4秒）の間隔制限を設ける。猫ボイスは押下開始から同じゴロゴロ音をループし、値変更時に再起動しない。SettingsVolumePreviewPointerは既存Sliderへ接続するマウス/タッチ操作の橋渡しで、操作元pointer IDの解放/Drag終了、Slider無効化、設定終了、フォーカス喪失、アプリ停止でプレビューを停止する。本編のゴロゴロ音Sourceは操作しない。固定UIの形状や配置は変更しない。

## 保存と検証

PlayerPrefsのNekolpos.Settings.v1へversion=1のJSONを保存。表示モード/最後のFullscreen/寸法/4音量/感度倍率/上下反転/VSyncを含む。ゲーム進行のProgressSaveStoreとは分離する。新キーがない場合のみOpenBeta.Audio.BgmMuted/SeMutedを0/100%へ移行し、旧SEミュートはCatVoiceにも引き継ぐ。保存時に旧キーを削除し、新キーが正本となる。破損/未対応versionは警告し初期値へ、非有限値や範囲外値・モード・解像度は検証して補正する。

GameSettingsTestsは初期値、移行、再Load、破損/将来version、未対応寸法・NaN、解像度重複、Fullscreen履歴、ショートカット抑制、dB変換、実Mixerと共通Panel/視点設定/Pause維持を検証する。検証結果と実Windows Player確認範囲は作業完了時にHANDOFF/Issueへ記録する。

### 2026-10-04の確認結果

正規Unity Test Runner XMLで追加15件、既存の歩行/Interaction/UI/安全移動53件が全件成功、Skip0。最終Slider寸法・Dropdown開閉の追加を含むPanel回帰1件も再成功。MainScene Playでタイトルから設定、Debug開始後のPause→設定→戻る（Pause維持、操作禁止、旧ボタン0）、BGM/SE/CatVoice routing、文字Overflow0、36pxのSlider handle、Scrollで最終VSync項目へ到達を確認。最終Play停止、元Scene/Prefabをこの作業から保存していない。

RuntimeのAssembly-CSharp 261ソースをWindows Release条件とWebGL条件でコンパイル成功、追加3RuntimeにWarning0。WebGL検証はUnityEngine.WebGLModuleのプラットフォーム用参照を追加し、既存WebGLInputの実APIを解決して実施した（Editor用参照だけの初回失敗は不採用）。WebGL実Playerビルド/ブラウザー動作は未実施。

Windows Release PlayerはMainSceneの検証用コピーから実Build成功、最終BuildはError0/Warning0、1963.39MB、29.34秒。実入力/Alt+Enter/長押し/Alt-Tab/Exclusive/実再起動/実聴感は未確認で、人によるWindows Player確認を残す。元Sceneの未保存編集を守るためsaveAsCopyを使用、検証用Assets内SceneはBuild後に削除。成果はC:/Users/shoho/.codex/tmp/issue38-settings-20261004（player/Nekolpos.exe、XML、条件コンパイル結果、設定画面画像）。UnitySkills RESTは未接続で、実Editor操作/Runner/Buildは別のUnity MCPブリッジを使用した。

## 固定UI編集（Issue #40）

SettingsPanel PrefabのWindow LayoutElement（Preferred Width/Height）、ModalShadeのVerticalLayoutGroup（Padding）、ContentのVerticalLayoutGroup（Spacing）をInspectorで編集する。縮小画面への収まりは標準LayoutGroup/CanvasScalerが担当し、LateUpdateでWindowのサイズを上書きしない。各Slider、Toggle、Dropdown、戻るButtonと数値TextはSettingsPanelのSerializeFieldへ接続済み。解像度候補だけをRuntimeで更新する。参照不足の場合はエラーを出して開かず、不完全なUIを自動生成しない。

Editor専用FixedUiMigration.csのNekolpos > UI > Migrate Missing Fixed UIは、欠けたUIと参照だけを追加する。既存SettingsPanel Prefabや配置済みUIの形状・フォントは再生成しない。移行後はSceneを保存する。開発ルールと対象外の生成箇所はdocs/FixedUiAuthoring.mdを参照。

## 漫符機能の撤去（2026-10-08）

ユーザー判断により漫符機能を撤去。会話制作ウィンドウの選択欄、表示処理、気づき表示・付属SE・追加待機、設定画面の表示切替を削除した。旧ReactionIcon/reaction_iconデータはCSV・Compiled Bundle互換性のため保持するが、表示には使用しない。
