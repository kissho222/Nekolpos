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

登録1件には出発用 `Start Director`、任意の到着用 `End Director`、`Change Home Location`、`Destination`、`Home Location Change Time` をまとめて設定する。`Home Location Change Time` には暗転完了の秒数を入力する。コードがその時刻で現在地・猫・カメラを更新し、Start終了後はEndを自動再生するので、地点ごとに位置更新やStart→End連結のSignal Asset / Signal Receiver Reactionを増やす必要はない。

登録するDirectorは通常会話用に専用で用意する。OP用の `timelineDirector` は登録しない。Timelineの `Animation Track` はNormal CatのAnimator、`Audio Track` は使用するAudioSourceへバインドする。暗転・明転用の既存Signalは共通の `Signal Receiver` を利用できる。再生中は既存の `CatPresentationModeController` をTimelineモードにして通常待機との競合を防ぎ、終了時に現在の基本会話拠点へ復帰する。

CSVでは、最後に実行したい `Action` 行の `action_id` へ次を指定する。

```text
play_conversation_timeline:MoveStartToDesk
```

Choiceの「はい」分岐先をこのAction行にすれば承認時に、通常会話の最終行をこのAction行にすれば発話完了後に再生する。

現在位置の確定はCSVの `move_home:Desk` と重ねて指定しない。登録項目の `Destination` と `Home Location Change Time` がその役割を担う。これにより、暗転前に位置やカメラが切り替わらず、指定時点で猫・カメラ・`H: Desk` が同時に更新される。

夜・翌朝・タブレット処理への自動接続は、TitleSceneの時間管理が遅延生成されるため、この段階では行わない。既存イベントから上記APIを呼び出して接続する。

## Timeline によるシーン間の場所遷移

`TitleScene > OpenBetaSystems > OpenBetaTitleBootstrap` は、Timeline の Signal Receiver から場所遷移を実行する入口を持つ。`Location Transition Timeline > Location Transition Scene Name` に、Build Settings に登録済みの遷移先シーン名（またはパス）を設定する。

`MoveStart` を再生する `PlayableDirector` と同じGameObjectへ `Signal Receiver` を追加し、各Reactionの対象に `OpenBetaSystems` を指定する。暗転開始のReactionは `OpenBetaTitleBootstrap.FadeToBlack`、暗転完了位置の `ApplyLocationChange` Signal のReactionは `OpenBetaTitleBootstrap.ApplyLocationChange` を選ぶ。`LoadLocationScene(string)` を選べば、Reactionごとに固定の遷移先名を指定することもできる。

`MoveStart` のような出発演出では、次の順で Marker を置く。

1. 演出開始時に `FadeToBlack` を呼ぶ。
2. 画面が完全に暗転した位置で `ApplyLocationChange` を呼ぶ。

`ApplyLocationChange` は設定済みのシーンを `LoadSceneMode.Single` で読み込む。同じTimelineで二重に発火しても、同一の `OpenBetaTitleBootstrap` からは最初の要求だけを受け付ける。シーン名未設定またはBuild Settings未登録の場合はロードせず、Consoleに警告を出す。

暗転・明転もTimelineのSignalでタイミングを管理できる。`FadeToBlack` と `FadeFromBlack` は `Location Transition Fade Seconds` の時間を使い、`SetTransitionOpaque` と `SetTransitionTransparent` は即時切替を行う。暗転時間を伸ばす場合は同項目を調整し、`ApplyLocationChange` のMarkerを暗転完了後に置く。
