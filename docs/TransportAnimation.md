# 2D運搬アニメーション（Issue #66）

## 素材と合成

正本は `Assets/Picture/きつねねね/ネルコロードアニメーション`。原PNG96枚は全て2560×1440 RGBA、16レイヤー各1～6。GIF7本は1280×720、24フレームで、PNG1～6を4フレームずつ保持する1秒ループ。保持時間は順に0.16/0.17/0.17/0.16/0.17/0.17秒。

PNGをSingle Sprite／FullRectで導入する。トリミングしない。同じ2560×1440のキャンバスを共通Rectへ描画する。原画像は変更しない。Import上限2048、MipMapなし、Read/Writeなし、CompressedHQ。上限による縮小は全レイヤー共通で、元の位置・縦横比を維持する。

見本の基本合成順は **ネルコスタンダード → 影（乗算）→ ネルコ線画 → プレイヤー → 必要時のネルコ不機嫌線**。スタンダードは茶毛・白部分・トラ模様・耳ピンクを含む着色済みの層で、線画と影は別。影を通常Alpha合成すると後ろ足の毛色を置換するため、専用 `Nekolpos/UI/TransportMultiply` Shaderを使う。ShaderはこのマスクなしOverlay専用で、Mask/RectMask2D付きUIへ流用しない。

| ID | 見本 | プレイヤー層 | 不機嫌線 |
|---|---|---|---|
| Basket | カゴ.gif | プレイヤーカゴ | なし |
| Collar | 首襟.gif | プレイヤー服襟 | なし |
| Feet | 足吊り.gif | プレイヤー吊り | なし |
| FeetWeary | 足咥え_やれやれネルコ.gif | プレイヤー吊り | あり |
| Head | 頭.gif | プレイヤー頭 | なし |
| HeadWeary | 頭咥え_やれやれネルコ.gif | プレイヤー頭 | あり |
| Back | 背中.gif | プレイヤー背中 | なし |

ベース白毛、ベース茶毛、ハチワレ、トラ模様、白1、白2、耳ピンクも全て導入・参照し、Layers/Presetsから使用を調整できる。白1は口元・足先・尾先の限定した白部分、白2は腹部・脚まで広い白部分。ベースや着色済みスタンダードとの組合せはInspectorで確認する。既存の毛色保存データは対象コード内に確認できず、外見カスタマイズ・保存方式は新設しない。

## Sceneと再生入口

本編 `MainScene` に `Assets/Prefab/TransportAnimationView.prefab` を配置。独立ScreenSpaceOverlay CanvasのsortingOrder=2000なので、OpenBetaCanvas=100上のFadeControllerがBringToFrontしても上に描画できる。GraphicRaycasterなし、CanvasGroupは入力をブロックしない。移動中の入力制御は従来のフェードが担う。

`MotionRoot > DisplayBounds` のRectTransformで位置と表示領域、`Layers`のAspectRatioFitterで16:9を保持する。各ImageのRectTransform・色・Material、ViewのLayers（Image/6 Sprite）、Presets（背面からのID順/6保持時間/Loop）をInspectorで調整する。Runtimeは固定レイアウトを生成・上書きせず、横移動の間だけ共通親の位置を動かして終了時に戻す。

## 追加コメントによる見た目・横移動（2026-10-09）

ユーザー指定により、全7パターンはGIF見本から意図的に左右反転する。`Layers.localScale.x=-1`でネルコ・模様・表情・Playerをまとめて反転し、右向きにする。原PNG/GIFは変更しない。反転を調整する場合も各ImageでなくLayersのScale Xを使う。

全6枚のスタンダードPNGの透過境界を測定し、絵の最大高さは1089/1440（約75.6%）。追加指示により、1920×1080基準の見える高さを500pxへ拡大した。透明余白込みの画像キャンバスは約661.16px高、DisplayBoundsの縦Anchor範囲は約0.1939～0.8061（高さ約61.2%）。解像度に応じて比例する。AspectRatioFitterのFitInParentにより幅が制約になる縦画面では小さくなる。大きさはDisplayBoundsの縦Anchor幅やLayersのScaleの絶対値で調整し、左右反転の符号を維持する。

横移動はViewの `Horizontal Travel` で設定する。

- `Motion Root`：全レイヤーを含む共通親。固定UIとしてPrefab上に配置する。
- `Travel Seconds`：初期2秒。6コマのframeSecondsとは独立。
- `Start/End Horizontal Position`：中央位置から親横幅を1とする相対位置。初期-1→+1で完全に画面外から入り、完全に画面外へ抜ける。
- `Travel Progress`：初期キー(0,0)→(0.5,0.5)→(1,1)、端の接線2.5、中央0.25。端は速く、中央は遅く、中央でも止まらない。一般的な端が遅いEaseInOutとは逆の速度変化。
- `Enable Horizontal Travel`：横移動を有効化。無効なら従来の静止位置で表示する。

上下の位置は変えない。全層の共通番号更新と横移動は同じunscaled経過時間を参照し、速度設定は独立する。`HasCompletedTravel`で右側への退出完了を確認できる。運搬Bootstrapは「移動先準備・Binding表示時間・横移動完了」の全てを待って表示を止めるため、短いBinding時間でも途中で消さない。プレビューは退出後も再生状態を維持するが絵は画面外にあり、Stop Previewで原点へ戻る。正常終了/中断/無効化/タイトル復帰でも共通親を開始前のInspector位置へ戻す。

Editorの `Nekolpos > UI > Apply Transport Travel Layout` はこの追加仕様のPrefab設定を明示的に適用する。旧Installの「既存Prefabを上書きしない」とは別の、ユーザー指定の見た目変更用操作である。再実行時もMotionRootを重複追加しない。

背景はIssue #67の `TransitionBackground.prefab` に接続した。場所移動中は現在の時間帯の空・太陽・ユーザー作成の前景を固定表示し、死亡等の通常黒フェードは維持する。詳細は `docs/TransitionBackground.md`。

## 場所移動の左→右ワイプ

追加コメント6080626494に従い、場所移動は既存FadeControllerの `WipeToBlack(float)` / `WipeFromBlack(float)` を使う。出発画面を覆う右境界も、到着画面を見せる左境界も左→右へ進む。初期時間は既存の `conversationLocationFadeSeconds` を使用しInspectorで調整する。Issue #67の共通背景接続後はRectMask2Dのクリップ範囲だけを動かし、PNGや太陽の構図を伸縮させない。背景なしの従来SceneはAnchorワイプへフォールバックする。

Scene正本のFadeOverlay RectTransformを遷移中だけ動かし、完了/即時透明化/無効化/タイトル復帰/通常フェードでの中断時に元のAnchor/Offsetを復元する。固定UIを新規生成しない。完全被覆時だけ `BecameOpaque` を通知し、その間だけ `IsOpaque` がtrueになるため、Bootstrapは固定秒数でなく従来のイベントでPlayer/猫/カメラの更新を同期する。退出後のワイプ完了で `BecameTransparent` を通知する。全処理はunscaled時間で進む。独立Canvasの運搬イラストはワイプより手前に維持する。時間切替・睡眠・死亡等の通常Fade入口は従来のAlphaフェードを維持する。

`TransportAnimationView.TryPlay(id)` / `StopPlayback()` が入口。全層を同じ番号・unscaled clockで更新。通常/タイトルはalpha=0で非表示。Play中に `Preview Preset` を変更し、Componentコンテキストメニュー `Preview Selected Preset (Play Mode)` と `Stop Preview` で7種を確認できる。確認用プレビューは運搬外でも明示操作により表示する。

Loopをオフにした場合は6コマ目を保持し、運搬側の表示区間終了またはStopPlaybackで非表示にする。

Editor専用 `TransportAnimationAuthoring` のメニュー `Nekolpos > UI > Install Transport Animation` は初回導入用。既存Viewのレイアウト/プリセットは再生成しない。Sceneは自動保存せず、設置後に保存する。既存Prefabの上書きも拒否する。

## 本編運搬への接続

### イベントからの共通呼び出し

会話以外のイベントもScene上の `NekolposGameBootstrap` 参照から呼ぶ。新Managerや独自のFade/Teleportを作らず、共通入口を使う。

```csharp
bool started = bootstrap.TryTransport("MoveToDesk");
// Tableへ戻る場合は "MoveToTable"。
// started=falseは未登録ID、無効Component、運搬中等で開始できなかったことを表す。
```

UnityEvent／Timeline Signalでは `Transport(string)` にBinding IDを設定する。会話CSVの既存 `play_conversation_timeline:<ID>` も同じ処理へ接続する。運搬経路を増やすときはInspectorの `Conversation Timeline Bindings` へ一度だけ登録し、各イベントはそのIDを共有する。

一時的なイベント固有Anchorでは、Inspector登録を増やさず要求を渡せる。

```csharp
bootstrap.TryTransport(new NekolposGameBootstrap.ConversationTimelineBinding
{
    id = "EventTransport",
    changeHomeLocation = true,
    destination = CatHomeLocation.Desk,
    playerDestination = eventDeskAnchor,
    expectedPlayerAreaId = "Desk",
    transportAnimationPreset = "Collar",
    transportAnimationSeconds = 2f
});
```

要求は開始時に複製し、呼び出し側の後続編集が実行中の移動を変えない。`IsTransporting`で完了・中断まで待てる。開始のboolは完了通知ではない。`CancelTransport()`で表示・SE・Fade・イベント購読を解除するが、既に暗転中に確定した移動先は巻き戻さない。多重要求はキューへ積まず拒否する。通常の運搬はView単体のTryPlayではなく、このBootstrap入口を使う。

共通入口追加後の検証は、実Playでの要求複製・多重拒否・キャンセル・再開始を含む17/17成功・Skip0（2026-10-09 11:44:11Z～11:44:12Z）。Scene切替で終了済み運搬まで復帰を試みていた問題を修正し、猫配置Controllerの破棄済み参照にもUnityのnull判定を使う。追加後のWindows/WebGL Buildは再実行していない。

`NekolposGameBootstrap.ConversationTimelineBinding` に `transportAnimationPreset` と `transportAnimationSeconds`、BootstrapにView参照を追加。ユーザー指定により **MoveToDesk・MoveToTableはCollar、表示時間2秒**。自動プリセット抽選・心理値による選択は追加しない。空IDなら従来どおり表示なし。死亡/睡眠/ケーブル/一般ロードには接続しない。

覆うワイプ開始 → `Transport Entrance Delay Seconds`後にView開始 → 完全暗転イベントで猫/Player/Area更新 → 姿勢/視線準備 → 画像終了予定の`Transport Exit Overlap Seconds`前に開くワイプ開始 → ワイプ・Binding表示時間・横移動の全完了後にView停止 → 任意のTabletカメラ、という順序。画像は覆うワイプを追い、開くワイプは画像の退出を追う。完全暗転前の場所更新はしない。

`MainScene > OpenBetaSystems > NekolposGameBootstrap > Location Transition Timeline`で開始遅延（初期0.15秒）と終了側の重なり（初期0.35秒）を調整する。開始遅延0なら同時開始、ワイプ時間以上なら従来の完全被覆後開始。重なり0なら従来の画像終了後に開く。画像終了予定はBindingの表示時間とViewの横移動時間の長い方を使う。開くワイプは場所更新・姿勢準備より前には開始せず、キャンセル時も既存の運搬Coroutine/イベント解除/View停止が両方を処理する。ワイプ自体の速度は既存`Conversation Location Fade Seconds`、画像の横移動速度はViewの`Travel Seconds`で独立して調整する。

表示開始時に以下のSEから1本をランダムに再生する。音だけが抽選対象。毎コマやループごとには鳴らさない。SettingsManagerのSE Mixerへ接続し、音量設定を反映する。AudioSourceは2D、Loopなし、表示終了/中断時に停止。

- `Assets/Audio/Xylophone04-mp3/Xylophone04/Xylophone04-15(Slow-Long-1-Up).mp3`（約1.80秒）
- `Assets/Audio/Xylophone04-mp3/Xylophone04/Xylophone04-17(Slow-Long-2-Up).mp3`（約1.67秒）
- `Assets/Audio/Xylophone04-mp3/Xylophone04/Xylophone04-19(Slow-Long-3-Up).mp3`（約1.78秒）

運搬中の再開始を拒否し、多重移動を防ぐ。正常終了/無効化/タイトル復帰/Scene破棄ではView停止、Sprite参照と音声をクリアし、既存のイベント解除と透明復帰を行う。素材不足・重複・ID不足・不正保持時間は警告して表示なしの既存移動へフォールバック。再生中の参照消失でも表示待ちを終了する。Pauseではフェード同様にunscaled timeで動き、片方だけの停止による待ち残りを避ける。

## 検証手順

1. MainSceneをPlayし、Table⇄Deskを実行。完全暗転後の前面表示・目的地Area/姿勢・明転後の復帰を確認。
2. 7プリセットをプレビューし、同一コマ・6→1・表情/運搬層の切替・GIF比較を確認。
3. 運搬中の連打、Time.timeScale=0、View/Bootstrap無効化、タイトル復帰、次回開始を確認。
4. テスト用にSpriteを欠損させ、警告後に既存移動が完了することを確認。空IDでも従来移動を確認。
5. `TransportAnimationTests` と関連場所移動テストを実行。Buildや実機で未確認の範囲はHANDOFF/Issue報告に明記。

PNGとGIFのオフライン比較は `output/transport-animation/multiply-comparison.png` と `multiply-audit.json`。全42コマのRGB平均絶対差は0.43～0.84/255で、GIFのパレット・縮小による色/輪郭差を含む。Unityの圧縮・Linear描画・実画面の比較は別途確認する。

## 2026-10-09の確認結果

ワイプ＋縦500への追加変更後、関連正規Runner46/46成功・失敗0・Skip0（12:26:29Z～12:26:35Z）。両境界の左→右進行、完全被覆/透明化のイベント通知、timeScale=0、中断/通常Fade割込のレイアウト復元を確認。MainScene実Play両方向で覆い/開き/画像のPNGおよび状態CSV保存、可視最大高500.1014/Canvas高1080.219、完了後の透明化・位置(0,0)を確認。Windows64 Build成功31.26秒・2,287,372,248bytes・Error0/Warning0。証跡は `wipe-results.xml`、`wipe-routes.csv`、`wipe-MoveToDesk/MoveToTable-*.png`、`wipe-build.txt`。今回WebGL/モバイル実機未確認。以下は前段階の確認記録で、現在の大きさは500基準。

追加コメント反映後は関連EditMode/実Play回帰25件すべて成功、失敗0・Skip0（12:09:29Z～12:09:31Z）。端→中央→端の速度差、中央での前進、Y位置不変、コマ再生との独立、停止/再開始時の位置復元、短い表示指定でも右側の退出まで暗転表示を維持することを確認した。証跡は `output/transport-animation/travel-results.xml`。

MainScene実Playで7種を順に再生してPNGを保存。全レイヤーの共通反転、黒背景、停止後の位置(0,0)を確認。GameView 821×462でCanvas高1080.219、元画像の透明余白を除いた最大表示高360.073（画面高の1/3）。証跡は `travel-Basket.png`～`travel-Back.png`。縦画面/モバイル実機は未確認。

追加後のWindows64 Buildも成功（28.85秒、2,287,370,200bytes、Error0/Warning0）。`output/transport-animation/travel-build.txt`へ保存。今回WebGL再Build・Windows Player画面操作・モバイル実機は実施していない。

MainSceneのPlayで7プリセットを実再生し、`output/transport-animation/unity-previews.png` に保存。実運搬のTable→DeskとDesk→Tableでは、表示中のIsOpaque=true、移動先Area、表示終了後の透明復帰を記録した。Desk→TableはTime.timeScale=0でも完了。指定3SE全ての抽選を30回の実再生で確認（`se-selection.txt`）。SEカテゴリへのMixer接続はRuntimeのSettingsManager経由である。

空プリセット・Sprite欠損は表示せず約0.74秒で従来移動へ復帰。View無効化、Bootstrap無効化でも表示alpha=0・Fade透明へ復帰。運搬中のPause Menu GoTitle（実Scene再読込）後は旧View破棄、新Viewの再生停止/alpha=0/CurrentFrame=-1・Fade透明を確認。多重開始はfalseで拒否。

新規の同期/切替/非Loop/欠損/実Playでの無効化テスト15件、関連場所移動テスト11件に成功（Skip0）。結果XMLは `output/transport-animation`。EditorのConsole Error0。

最終コードで両者をまとめて再実行し26/26成功・Skip0（2026-10-09 11:34:45Z～11:34:47Z）。正規Test RunnerのXML終了を確認後、Domain Reloadで残留したMCPのrunning jobは公式clear_stuckで解除した。

Windows64 ReleaseのMainScene実Build成功（46.74秒、2,287,367,800 bytes、Error0/Warning0）。出力は `output/transport-animation/windows/Nekolpos.exe`。Windows Player自体の画面操作は未実施。

WebGLのPlayerBuildInterface直接コンパイルは、Windows選択中のEditorでWebGLInput参照が欠け、失敗した。ゲームコードは変更せず、同APIが生成したWebGL向け依存DLL34本・実UnityEngine.WebGLModuleを参照し、Runtime268ソースをUnity同梱RoslynでUNITY_WEBGL条件コンパイルしてError0で成功。直接Roslynの警告はCS0649=239（主にInspector設定フィールド）、CS0169=15、依存netstandard版差CS1701=2。Unity通常コンパイラーと異なる警告設定であり、Warning0とは扱わない。全WebGL実Build、IL2CPP/Shaderコンパイル、ブラウザー操作、iOS/Android/縦画面の実機表示は未確認。リリース前の実機検証対象とする。
