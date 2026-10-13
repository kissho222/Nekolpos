# タブレット電源制御

`NekolposGameBootstrap` がタブレットの電源状態の正本である。`WakeTabletDisplay()` と `SleepTabletDisplay()` を含む単一の境界から状態を変更する。

- 電源ON時は `EmissionPanel` と `TabletCanvas` を有効化し、`TabletCanvas` を `CanvasGroup` で0.2秒かけてフェードインする。
- 電源OFF時は `TabletCanvas` を0.2秒かけてフェードアウトしてから、`TabletCanvas` と `EmissionPanel` を無効化する。
- `TabletCanvas` はタブレット配下から、`EmissionPanel` は同一シーン内から自動検出する。複数ある場合は `NekolposGameBootstrap` の該当フィールドへ明示的に割り当てる。
- `TabletInput` はエディターまたはDevelopment Buildでのみ Shift+P を受け付け、電源を切り替える。リリース版ではキーボード入力を要求しない。

`m_powerFadeDuration` は Inspector から変更でき、標準値は0.2秒である。

## 物理接触によるWakeと散歩中の操作

`TabletScreenWakeController` は画面用のTriggerで、点灯状態だけを管理する。カメラ・入力を含むタブレット操作状態とは別であり、画面が点いているだけで会話や散歩を中断しない。

- `NekolposGameBootstrap` は起動時に、`TabletCanvas` と同じワールド姿勢・大きさの `RuntimeTabletScreenWakeTrigger` を生成する。Canvasを消灯してもTriggerは残るため、消灯中に踏んで再点灯できる。
- Playerには `TabletWakeSource(Player)` を、通常猫の前後の足骨 `foot_f.L` / `foot_f.R` / `foot_b.L` / `foot_b.R` だけには小さな `SphereCollider` と `TabletWakeSource(CatPaw)` を実行時に付与する。猫の胴体、頭部、尻尾には付与しない。既存あくび `CatSimple_Lie_belly_loop_3` 中も前足が画面へ重なった時だけ同じ `WakeTablet()` が呼ばれる。
- `OnTriggerEnter` の接触開始だけでWakeする。接触中の連続呼び出しは行わず、Player・前後の足がすべて画面から離れ、かつタブレットCamera操作中でなければ、`NekolposGameBootstrap > Tablet World Wake Sleep Seconds`（既定8秒）後に消灯する。再接触時はタイマーを解除する。
- `TabletWorldInteractable` は点灯中だけ、既存の散歩用 `PlayerInteractor` に `E：操作する` を公開する。消灯中はプロンプトも操作も出ない。操作開始時にはアプリPanel・アプリ内状態を変更せず、既存の画面のままタブレットCameraへ遷移する。
- 散歩から開いた場合は `PlayerWalkModeController` がPlayerCameraと移動入力を一時停止し、TabletInteractionChangedで猫のAgentも同フレームに停止する。PowerIconで戻った後は、通常会話へ遷移せず、元の散歩Camera・入力・UI抑止状態へ復帰する。

この経路のE入力は既存の暫定PCデバッグ操作であり、モバイル版では同じ `TabletWorldInteractable.TryInteract` をタップ操作から呼ぶことで置き換える。ゲーム機能としてキーボード入力を必須にしない。

## 散歩からの復帰（Issue #29）

`NekolposGameBootstrap.CurrentTabletEntryContext` は `Conversation` と `WalkMode` を区別する。散歩側の `TryBeginTabletInteraction()` はPlayerのWorld位置・回転とPlayerCameraのWorld/Local姿勢を保存する。閲覧への移行と終了時の補間では、描画CameraとCameraEventPivotのオフセットを考慮して保存した散歩視点を使う。会話固定視点への補間を挟まない。

補間完了後は既存Safety Controllerで保存位置へ安全に戻し、視点操作の内部角度・CameraのLocal姿勢・Areaを同期してからPlayerCameraと歩行入力を再開する。LastSafePositionは上書きしない。無効になった会話CameraのPivot姿勢はPlayerCameraへ切り替えた後で保存状態へ戻す。会話・夜の日記から入った場合は従来の復帰経路を維持する。

散歩からのPowerIcon終了では`SetHomeLocation(Desk)`を呼ばず、ネルコの閲覧開始位置・回転を維持する。閲覧中は猫の移動経路を解除して静止させ、Follow/Wait指定は保存する。通常会話からPowerIconで終了する場合のDeskへの移動は維持する。

夜の通常日記を確定済みの場合は、PowerIconの消灯とカメラ復帰補間が完了してからDiaryCalendarControllerへ入眠開始を通知する。カレンダーへ戻っただけ、または日記アプリをCloseしただけでは翌朝へ進めない。夜の時間帯へ入る時点でDeskへ配置済みのため、ちゃぶ台からタブレットへ移動する途中画面を夜の日記の開始として見せない。入眠から翌朝までの会話/共通フェードはDialogueManagerが所有する。
