# 基本会話拠点

通常会話時の猫の基本位置は `CatPositionController` が管理する `CatHomeLocation` で選択する。現在の基本会話地点の正本は既存の `CatPositionController.CurrentHomeLocation` であり、会話Conditionなど他システムは `CatPositionController.CurrentConversationLocation` を通じて同じ値を参照する。拠点の切替時だけ更新され、Transform座標・GameObject名・距離から現在地を推測しない。

## 拠点

- `CatHomeLocation.Table`: ちゃぶ台側。既定値およびゲーム開始時の位置。
- `CatHomeLocation.Desk`: 机側。`Normal_Cat_Home_Desk` を使用する。
- `CatHomeLocation.Bathroom`: 浴室側。
- `CatHomeLocation.Bed`: ベッド周辺。

`Assets/Scenes/TitleScene.unity` には、次の2つのTransformを配置する。

- `Normal_Cat_Home`
- `Normal_Cat_Home_Desk`

机側の位置・回転はSceneまたはInspectorで調整する。追加時の仮位置は既存のちゃぶ台側と同じ位置にしている。浴室・ベッドを使用するSceneでは、`CatPositionController` の `Bathroom Home Anchor` / `Bed Home Anchor` を割り当てる。既存のTable/Deskアンカー設定はそのまま利用できる。

机側へ切り替えた時の `CameraEventPivot` は、現在 `Vector3(0.801, 0.74, -0.56)` へ移動する。`OpenBetaTitleBootstrap` の Inspector にある `Desk Home Camera Local Position` で調整でき、`Preview Desk Camera Pose In Editor` がオンなら編集モードでも即時反映される。再生中は現在の拠点が机側なら、Inspectorでの変更が即時反映される。

## デバッグ切り替え

デバッグ開始時に表示される `Nekolpos Debug Window` の「基本会話拠点」から、ちゃぶ台側と机側を切り替えられる。切り替えは既存のフェードOverlayを使い、フェードアウト・拠点変更・フェードインをそれぞれ1秒で行う。

## 公開API

`CatPresentationModeController` または `OpenBetaTitleBootstrap` から次を呼び出せる。

```csharp
SetHomeLocation(CatHomeLocation.Table);
SetHomeLocation(CatHomeLocation.Desk);
SetHomeLocation(CatHomeLocation.Bathroom);
SetHomeLocation(CatHomeLocation.Bed);
```

短縮APIとして `MoveHomeToTable()` と `MoveHomeToDesk()` も利用できる。切り替え時は猫を選択した拠点へ即時配置し、その後の会話・待機・モーション復帰でも同じ拠点を維持する。

## 会話Conditionの現在位置タグ `H`

CSVの `condition` 列では、固定タグ `H:` で現在の基本会話地点を指定できる。位置Conditionがない既存行は、すべての拠点で引き続き有効である。`H:` を既存Conditionと併記する場合は `&&` でつなぐ。

```text
H: Desk
H: Desk Table Bathroom
H: !Desk
Affection >= 50 && H: Desk
```

- `H: Desk` はDeskだけで有効。
- 同じ `H:` 内の正のIDはOR判定。`H: Desk Table Bathroom` はBed以外で有効。
- `!` は否定。`H: !Desk` は、将来追加される拠点を含めDesk以外のすべてで有効。
- 正のIDと否定IDを混在させた場合、正のID群はOR、否定ID群はすべて除外条件として扱う。
- 使用できるIDは `Desk` / `Table` / `Bathroom` / `Bed`。未定義IDはWarningを出し、その `H:` 条件は不成立になる。ゲーム進行は停止しない。

`H:` は通常会話、BasicSystemDialogue、選択肢の分岐先を含め、共通のCondition評価処理で判定される。新しい拠点を追加する場合は `CatHomeLocation` と対応アンカーを追加すれば、`!Desk` の意味を変更せずに利用できる。

会話プレビュー・編集ツールの `condition_A`〜`D` プルダウンには、`H: Desk` / `H: Table` / `H: Bathroom` / `H: Bed` が通常Conditionと同じ候補として含まれる。各プルダウンの上にある `A 不成立`〜`D 不成立` は個別に有効化でき、通常Conditionは `!Rain`、位置Conditionは `H: !Table` の形式で保存する。たとえば `!Rain && !Spring && !Morning && H: !Table` は「雨以外かつ春以外かつ朝以外かつテーブル以外」を表す。この行は既存の空き領域を使うため、ウィンドウのサイズは変わらない。複数位置を同じ `H:` に指定した既存Conditionは編集対象にせず、保持中のConditionとして残る。

`TalkSource/TalkCSV` 配下のCSVをDialogue Previewから保存した場合は、同名の `Assets/Resources/TalkData/*.bytes` も同時に暗号化更新する。ゲーム実行時はこのバイト資産を読むため、CSVだけを更新して位置Conditionが古い実行データへ反映されない状態を防ぐ。

## 会話から基本会話地点を移動する

`response_type` が `Action` の行は、`action_id` に `move_home:Desk` のように指定できる。使用可能なIDは `Desk` / `Table` / `Bathroom` / `Bed`。既存の `OpenBetaTitleBootstrap.SetHomeLocation` を通るため、猫・カメラ・現在地Conditionを同時に更新する。位置IDが未定義ならWarningを出し、ゲーム進行は停止しない。

`timeline_sequence:` はAnimation列だけで使用する。登録済みSequenceに対し、`timeline_sequence:<SequenceId>Start` または `timeline_sequence:<SequenceId>End` の形式で指定する。場所移動自体には使用しない。

## 会話から通常Timelineを再生する

暗転、Audio、Signal、位置変更を含む場所移動演出は `DialogueTimelineSequenceController` へ登録しない。`TitleScene > OpenBetaSystems > OpenBetaTitleBootstrap > Conversation Timeline Actions` に、通常の `PlayableDirector` をIDとともに登録する。

登録1件には出発用 `Start Director`、任意の到着用 `End Director`、`Change Home Location`、`Destination` をまとめて設定する。Directorと旧Sceneに残る `Home Location Change Phase` / `Home Location Change Time` は互換用の表示項目であり、基本会話地点の移動では再生・参照しない。コードがフェードの**暗転完了イベント**を受けてから現在地・猫・カメラを直接更新する。これにより地点ごとに位置更新やStart→End連結のSignal Asset / Signal Receiver Reactionを増やす必要はない。

出発・到着Directorの既存参照はScene互換のため保持するが、基本会話地点の移動では再生しない。暗転・明転用の既存Signalは共通の `Signal Receiver` を利用できるが、会話から起動する移動中だけはコード側がフェードを所有し、Signalによる二重のフェード要求を抑止する。移動中は既存の `CatPresentationModeController` を一時的にTimelineモードへ切り替え、暗転中に待機姿勢へ復帰させる。

CSVでは、最後に実行したい `Action` 行の `action_id` へ次を指定する。

```text
play_conversation_timeline:MoveToDesk
```

Choiceの「はい」分岐先をこのAction行にすれば承認時に、通常会話の最終行をこのAction行にすれば発話完了後に再生する。

現在位置の確定はCSVの `move_home:Desk` と重ねて指定しない。登録項目の `Destination` がその役割を担う。会話からの拠点移動は常に次の順で進むため、フェード秒数を変更しても暗転途中・明転途中に猫のワープや視線の遅れが見えない。

1. 移動開始後、コードが暗転を開始する。
2. `FadeController.BecameOpaque`（完全暗転イベント）を待つ。
3. 遷移先アンカーへ猫と拠点カメラを配置する。
4. 待機姿勢とカメラ優先の視線Rigを更新する。Animation Riggingの評価のためフレーム終端を1回待って再更新する。
5. 明転を開始する。基本会話地点の暗転・明転は各0.5秒（合計最低1秒）にし、共有Directorの再生時間やタブレットカメラ補間は待機しない。

この経路での完了条件は、`FadeController.BecameTransparent`（明転完了イベント）を受けた後である。`WaitForSeconds` による暗転時間の推測は使わない。

夜・翌朝・タブレット処理への自動接続は、TitleSceneの時間管理が遅延生成されるため、この段階では行わない。既存イベントから上記APIを呼び出して接続する。

## タブレット閲覧カメラ

`TitleScene > PlayerCameraRoot` 配下の `TabletCameraPoint` が、タブレット閲覧用の実カメラ姿勢を保持する。現在の位置は `(0.867, 0.851, -1.321)`、回転は `(71.9, 90, 0)` であり、`OpenBetaTitleBootstrap > Tablet Camera > Tablet Camera Point` から参照する。座標をコードへ固定値として持たないため、演出調整時はこのTransformだけを動かす。

`Conversation Timeline Actions` の対象行で `Enter Tablet Camera After Playback` を有効にすると、猫の姿勢・視線更新と明転完了の後、Desk/Tableの完成状態を1フレーム表示してから `CameraEventPivot` がこのTransformへ0.4秒で補間移動する。`MoveToDesk` はこの設定を有効にしている。したがって、明転中に猫のワープ・視線遅れ・タブレット画面への切替が混ざらない。タブレットカメラ到着後はカーソルを解除してUIを操作できる。

タブレット本体の `TabletCanvas` と `EmissionPanel` は通常時に消灯する。`OpenBetaTitleBootstrap` が閲覧カメラの到着時だけ両方を有効化し、`ExitTabletCameraView()` の開始時・タイトル遷移の中断時には即座に無効化する。Canvasはレイアウトを左右反転させない既存のY=180回転を維持し、`DisplayArea`の表面かつカメラ側となるローカルZ=+0.054に置く。`EmissionPanel`（Z=+0.051）よりわずかに前面へ置くため、DisplayAreaのMesh Rendererに隠れない。これによりMesh Rendererを無効化・マテリアル差し替えしない。`TabletDisplayController` は消灯時にCanvasGroupを透明へ戻すため、電源ONではActive化だけでなく`TabletCanvas`のCanvasGroupを`alpha=1`・操作可能へ復帰させる。これにより起動直後や会話中に画面が常時点灯して見える状態と、Activeでも黒いままになる状態の両方を防ぐ。

タブレット画面内の `PowerIcon` は実行時にButtonとして接続される。押下するとタブレット画面だけが黒へ0.2秒でフェードし、黒になった後に画面を消灯する。Desk側の通常会話カメラへの復帰は即時スナップせず、`f(t)=(1+s)t/(1+st)` の反比例型イージングで補間する。既定の強さ `s=2` は開始直後を速く、到着前をゆっくりにする。`OpenBetaTitleBootstrap > Tablet Camera > Tablet Power Return Reciprocal Strength` で強さを調整できる。ゲーム画面全体のフェードは使わない。

通常視点を操作するコンポーネントを導入した場合は、同じ欄の `Tablet Camera Input Controllers` に登録する。閲覧開始時に有効状態を保存して停止し、`OpenBetaTitleBootstrap.ExitTabletCameraView()` で元のカメラ姿勢・入力状態・カーソル状態へ安全に戻す。現在のTitleSceneには停止対象となるFPS視点操作コンポーネントがないため、この配列は空でよい。

`OpenBetaCanvas/TabletButton` は `OpenBetaTitleBootstrap` が自動接続する。ボタンは現在の基本会話地点が `Desk` で、通常会話の入力欄が表示・操作可能な入力待ち中だけ有効になる。台詞の表示中、選択肢中、会話Timeline中、タブレットカメラ遷移中、またはタブレット閲覧中は非表示である。押すと会話入力UIを一時的に閉じてタブレットカメラへ遷移し、`ExitTabletCameraView()` の完了後に通常入力待ちへ戻る。

## Timeline によるシーン間の場所遷移

`TitleScene > OpenBetaSystems > OpenBetaTitleBootstrap` は、Timeline の Signal Receiver から場所遷移を実行する入口を持つ。`Location Transition Timeline > Location Transition Scene Name` に、Build Settings に登録済みの遷移先シーン名（またはパス）を設定する。

`MoveStart` を再生する `PlayableDirector` と同じGameObjectへ `Signal Receiver` を追加し、各Reactionの対象に `OpenBetaSystems` を指定する。暗転開始のReactionは `OpenBetaTitleBootstrap.FadeToBlack`、暗転完了位置の `ApplyLocationChange` Signal のReactionは `OpenBetaTitleBootstrap.ApplyLocationChange` を選ぶ。`LoadLocationScene(string)` を選べば、Reactionごとに固定の遷移先名を指定することもできる。

`MoveStart` のような出発演出では、次の順で Marker を置く。

1. 演出開始時に `FadeToBlack` を呼ぶ。
2. 画面が完全に暗転した位置で `ApplyLocationChange` を呼ぶ。

`ApplyLocationChange` は設定済みのシーンを `LoadSceneMode.Single` で読み込む。同じTimelineで二重に発火しても、同一の `OpenBetaTitleBootstrap` からは最初の要求だけを受け付ける。シーン名未設定またはBuild Settings未登録の場合はロードせず、Consoleに警告を出す。

暗転・明転もTimelineのSignalでタイミングを管理できる。`FadeToBlack` と `FadeFromBlack` は `Location Transition Fade Seconds` の時間を使い、`SetTransitionOpaque` と `SetTransitionTransparent` は即時切替を行う。暗転時間を伸ばす場合は同項目を調整し、`ApplyLocationChange` のMarkerを暗転完了後に置く。
