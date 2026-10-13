# 基本会話拠点

通常会話時の猫の基本位置は `CatPositionController` が管理する `CatHomeLocation` で選択する。猫の基本会話地点の正本は `CatPositionController.CurrentHomeLocation` であり、猫・カメラ配置は `CatPositionController.CurrentConversationLocation` を参照する。プレイヤー現在地とは別状態であり、`MainScene`の会話Condition `H:` は `PlayerAreaTracker.CurrentAreaId` を正本とする。どちらもTransform座標・GameObject名・距離から推測しない。

## 拠点

- `CatHomeLocation.Table`: ちゃぶ台側。既定値およびゲーム開始時の位置。
- `CatHomeLocation.Desk`: 机側。`Normal_Cat_Home_Desk` を使用する。
- `CatHomeLocation.Bathroom`: 浴室側。
- `CatHomeLocation.Bed`: ベッド周辺。

`Assets/Scenes/MainScene.unity` には、次の2つのTransformを配置する。

- `Normal_Cat_Home`
- `Normal_Cat_Home_Desk`

机側の位置・回転はSceneまたはInspectorで調整する。追加時の仮位置は既存のちゃぶ台側と同じ位置にしている。浴室・ベッドを使用するSceneでは、`CatPositionController` の `Bathroom Home Anchor` / `Bed Home Anchor` を割り当てる。既存のTable/Deskアンカー設定はそのまま利用できる。

机側へ切り替えた時の `CameraEventPivot` は、現在 `Vector3(0.801, 0.74, -0.56)` へ移動する。`NekolposGameBootstrap` の Inspector にある `Desk Home Camera Local Position` で調整でき、`Preview Desk Camera Pose In Editor` がオンなら編集モードでも即時反映される。再生中は現在の拠点が机側なら、Inspectorでの変更が即時反映される。

## デバッグ切り替え

デバッグ開始時に表示される `Nekolpos Debug Window` の「基本会話拠点」から、ちゃぶ台側と机側を切り替えられる。切り替えは既存のフェードOverlayを使い、フェードアウト・拠点変更・フェードインをそれぞれ1秒で行う。

## 公開API

`CatPresentationModeController` または `NekolposGameBootstrap` から次を呼び出せる。

```csharp
SetHomeLocation(CatHomeLocation.Table);
SetHomeLocation(CatHomeLocation.Desk);
SetHomeLocation(CatHomeLocation.Bathroom);
SetHomeLocation(CatHomeLocation.Bed);
```

短縮APIとして `MoveHomeToTable()` と `MoveHomeToDesk()` も利用できる。切り替え時は猫を選択した拠点へ即時配置し、その後の会話・待機・モーション復帰でも同じ拠点を維持する。

## 会話Conditionの現在位置タグ `H`

CSVの `condition` 列では、固定タグ `H:` でプレイヤーの現在Areaを指定できる。位置Conditionがない既存行は、すべてのAreaで引き続き有効である。`H:` を既存Conditionと併記する場合は `&&` でつなぐ。

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

`H:` は通常会話、BasicSystemDialogue、選択肢の分岐先を含め、共通のCondition評価処理で判定される。MainSceneでは`PlayerAreaTracker`、Trackerを持たない旧テストSceneでは明示的な互換処理として猫ホーム位置を使う。新しい`H:` IDを使う場合は、AreaVolume設定に加えて`CatHomeLocation`にも同じIDを定義し、未定義ID判定との整合を取る。

会話プレビュー・編集ツールの `condition_A`〜`D` プルダウンには、`H: Desk` / `H: Table` / `H: Bathroom` / `H: Bed` が通常Conditionと同じ候補として含まれる。各プルダウンの上にある `A 不成立`〜`D 不成立` は個別に有効化でき、通常Conditionは `!Rain`、位置Conditionは `H: !Table` の形式で保存する。たとえば `!Rain && !Spring && !Morning && H: !Table` は「雨以外かつ春以外かつ朝以外かつテーブル以外」を表す。この行は既存の空き領域を使うため、ウィンドウのサイズは変わらない。複数位置を同じ `H:` に指定した既存Conditionは編集対象にせず、保持中のConditionとして残る。

`TalkSource/TalkCSV` 配下のCSVをDialogue Previewから保存した場合は、同名の `Assets/Resources/TalkData/*.bytes` も同時に暗号化更新する。ゲーム実行時はこのバイト資産を読むため、CSVだけを更新して位置Conditionが古い実行データへ反映されない状態を防ぐ。

## 会話から基本会話地点を移動する

`response_type` が `Action` の行は、`action_id` に `move_home:Desk` のように指定できる。使用可能なIDは `Desk` / `Table` / `Bathroom` / `Bed`。既存の `NekolposGameBootstrap.SetHomeLocation` を通じて猫・カメラを更新するが、プレイヤーAreaと`H:`条件は更新しない。位置IDが未定義ならWarningを出し、ゲーム進行は停止しない。

`timeline_sequence:` はAnimation列だけで使用する。登録済みSequenceに対し、`timeline_sequence:<SequenceId>Start` または `timeline_sequence:<SequenceId>End` の形式で指定する。場所移動自体には使用しない。

## 会話から通常Timelineを再生する

暗転、Audio、Signal、位置変更を含む場所移動演出は `DialogueTimelineSequenceController` へ登録しない。`MainScene > OpenBetaSystems > NekolposGameBootstrap > Conversation Timeline Actions` に、通常の `PlayableDirector` をIDとともに登録する。

登録1件には出発用 `Start Director`、任意の到着用 `End Director`、`Change Home Location`、`Destination`、必要に応じて `Player Destination` と `Expected Player Area Id` をまとめて設定する。`Player Destination`は意図的な移動後のPlayerを置くTransformであり、猫の拠点やArea IDの正本ではない。Directorと旧Sceneに残る `Home Location Change Phase` / `Home Location Change Time` は互換用の表示項目であり、基本会話地点の移動では再生・参照しない。コードがフェードの**暗転完了イベント**を受けてから猫・カメラを更新し、続けてPlayerを安全に再配置する。これにより地点ごとに位置更新やStart→End連結のSignal Asset / Signal Receiver Reactionを増やす必要はない。

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
4. `Player Destination`が設定されていれば、同じ完全暗転中に`PlayerAreaTransitionController.TeleportImmediately`でPlayerを移動する。`Expected Player Area Id`が空欄なら`Destination`名を期待Areaとして再評価する。別のフェードは開始しない。
5. 待機姿勢とカメラ優先の視線Rigを更新する。Animation Riggingの評価のためフレーム終端を1回待って再更新する。
6. 明転を開始する。基本会話地点の暗転・明転は各0.5秒（合計最低1秒）にし、共有Directorの再生時間やタブレットカメラ補間は待機しない。

MainSceneの`MoveToDesk`/`MoveToTablet`には`PlayerTransitionPoints/PlayerSpawn_Desk`と`Desk`、`MoveToTable`には`PlayerTransitionPoints/PlayerSpawn_Table`と`Table`をInspectorから割り当てる。`play_conversation_timeline:MoveToDesk`はデスクへ移動して通常会話、`play_conversation_timeline:MoveToTablet`はデスクへ移動してタブレット操作画面へ進む。既存タブレット会話00350のActionは`MoveToTablet`を使い、机へ戻る会話は`MoveToDesk`を維持する。Anchor未設定時はWarningを出して猫側の既存移動だけを継続し、Playerを座標原点へ移動しない。

この経路での完了条件は、`FadeController.BecameTransparent`（明転完了イベント）を受けた後である。`WaitForSeconds` による暗転時間の推測は使わない。

時間経過の見回し・あくび中に移動を始めると、待機バリエーションのCoroutineとその復帰処理は中断される。`CatPresentationModeController.ResumeIdleAfterTimeline`はスケジューラーを再起動する前に、`CatMotionController.PlayIdle`で基本待機（`CatSimple_Lie_belly_loop_1`）とモーション所有状態を復元し、一時的な頭部角度制限と視線Weightも解除する。移動開始時には旧視線再更新Coroutineも停止する。中断された`loop_2`/`loop_3`を残したまま再開すると、基本待機中だけ許可する次の時間経過モーションが永久に待機するため、管理フラグだけの復帰は行わない。通常時の視線追従は維持し、見回し中のWeightフェードとあくび中の頭部制限は従来どおり適用する。

夜・翌朝の場所確定はBootstrapがTimeManager.OnTimeAdvancedを購読して接続する。同日で夜へ入る時はDesk、日付変更時はTableのPlayer Destinationへ揃える。日記確定後はカレンダーを閲覧でき、タブレット電源終了とカメラ復帰後に入眠会話→完全暗転中の翌朝移動→明転→朝挨拶へ進む。詳細はDiaryJournal.mdを参照する。

## タブレット閲覧カメラ

`MainScene > PlayerCameraRoot` 配下の `TabletCameraPoint` が、タブレット閲覧用の実カメラ姿勢を保持する。現在の位置は `(0.867, 0.851, -1.321)`、回転は `(71.9, 90, 0)` であり、`NekolposGameBootstrap > Tablet Camera > Tablet Camera Point` から参照する。座標をコードへ固定値として持たないため、演出調整時はこのTransformだけを動かす。

`Conversation Timeline Actions` の対象行で `Enter Tablet Camera After Playback` を有効にすると、猫の姿勢・視線更新と明転完了の後、Desk/Tableの完成状態を1フレーム表示してから `CameraEventPivot` がこのTransformへ0.4秒で補間移動する。`MoveToTablet`だけこの設定を有効にし、`MoveToDesk`は無効にする。したがって、明転中に猫のワープ・視線遅れ・タブレット画面への切替が混ざらない。タブレットカメラ到着後はカーソルを解除してUIを操作できる。

タブレット本体の `TabletCanvas` と `EmissionPanel` は通常時に消灯する。`NekolposGameBootstrap` が閲覧カメラの到着時だけ両方を有効化し、`ExitTabletCameraView()` の開始時・タイトル遷移の中断時には即座に無効化する。Canvasはレイアウトを左右反転させない既存のY=180回転を維持し、`DisplayArea`の表面かつカメラ側となるローカルZ=+0.054に置く。`EmissionPanel`（Z=+0.051）よりわずかに前面へ置くため、DisplayAreaのMesh Rendererに隠れない。これによりMesh Rendererを無効化・マテリアル差し替えしない。`TabletDisplayController` は消灯時にCanvasGroupを透明へ戻すため、電源ONではActive化だけでなく`TabletCanvas`のCanvasGroupを`alpha=1`・操作可能へ復帰させる。これにより起動直後や会話中に画面が常時点灯して見える状態と、Activeでも黒いままになる状態の両方を防ぐ。

タブレット画面内の `PowerIcon` は実行時にButtonとして接続される。押下するとタブレット画面だけが黒へ0.2秒でフェードし、黒になった後に画面を消灯する。Desk側の通常会話カメラへの復帰は即時スナップせず、`f(t)=(1+s)t/(1+st)` の反比例型イージングで補間する。既定の強さ `s=2` は開始直後を速く、到着前をゆっくりにする。`NekolposGameBootstrap > Tablet Camera > Tablet Power Return Reciprocal Strength` で強さを調整できる。ゲーム画面全体のフェードは使わない。

通常視点を操作するコンポーネントを導入した場合は、同じ欄の `Tablet Camera Input Controllers` に登録する。閲覧開始時に有効状態を保存して停止し、`NekolposGameBootstrap.ExitTabletCameraView()` で元のカメラ姿勢・入力状態・カーソル状態へ安全に戻す。現在のMainSceneには停止対象となるFPS視点操作コンポーネントがないため、この配列は空でよい。

`OpenBetaCanvas/TabletButton` は `NekolposGameBootstrap` が自動接続する。ボタンは現在の基本会話地点が `Desk` で、通常会話の入力欄が表示・操作可能な入力待ち中だけ有効になる。台詞の表示中、選択肢中、会話Timeline中、タブレットカメラ遷移中、またはタブレット閲覧中は非表示である。押すと会話入力UIを一時的に閉じてタブレットカメラへ遷移し、`ExitTabletCameraView()` の完了後に通常入力待ちへ戻る。

## Timeline によるシーン間の場所遷移

`MainScene > OpenBetaSystems > NekolposGameBootstrap` は、Timeline の Signal Receiver から場所遷移を実行する入口を持つ。`Location Transition Timeline > Location Transition Scene Name` に、Build Settings に登録済みの遷移先シーン名（またはパス）を設定する。

`MoveStart` を再生する `PlayableDirector` と同じGameObjectへ `Signal Receiver` を追加し、各Reactionの対象に `OpenBetaSystems` を指定する。暗転開始のReactionは `NekolposGameBootstrap.FadeToBlack`、暗転完了位置の `ApplyLocationChange` Signal のReactionは `NekolposGameBootstrap.ApplyLocationChange` を選ぶ。`LoadLocationScene(string)` を選べば、Reactionごとに固定の遷移先名を指定することもできる。

`MoveStart` のような出発演出では、次の順で Marker を置く。

1. 演出開始時に `FadeToBlack` を呼ぶ。
2. 画面が完全に暗転した位置で `ApplyLocationChange` を呼ぶ。

`ApplyLocationChange` は設定済みのシーンを `LoadSceneMode.Single` で読み込む。同じTimelineで二重に発火しても、同一の `NekolposGameBootstrap` からは最初の要求だけを受け付ける。シーン名未設定またはBuild Settings未登録の場合はロードせず、Consoleに警告を出す。

暗転・明転もTimelineのSignalでタイミングを管理できる。`FadeToBlack` と `FadeFromBlack` は `Location Transition Fade Seconds` の時間を使い、`SetTransitionOpaque` と `SetTransitionTransparent` は即時切替を行う。暗転時間を伸ばす場合は同項目を調整し、`ApplyLocationChange` のMarkerを暗転完了後に置く。


下品語フィルタの宛先はVulgarLanguageの第2列であり、現在の共通宛先は「ひわい」。DialoguePreview_Integratedの該当14行はregex_idを「ひわい」に揃える。別の意味キーに登録すると検知だけ成功して反応辞書が見つからなくなるため、暗号化TalkDataから実宛先の反応取得まで回帰検証する。2026-10-04修正はキーのみ、台詞・Pattern・Order・Conditionは変更していない。
