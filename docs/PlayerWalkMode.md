# 散歩モード

`PlayerWalkModeController` は、自由会話のChoice Action `walk` からPlayer自由歩行を開始し、暫定終了キー `P` で通常会話へ戻す専用Controllerである。散歩開始時だけ現在の会話拠点に対応するリスポーン地点へPlayerを安全に配置し、`LastSafePosition`と`PlayerAreaTracker`を同期する。終了時はPlayer位置を変えない。

## MainSceneの設定

`Player`へControllerを追加し、次をInspectorで明示参照する。

- 同じPlayerの`PlayerSafetyMovementController`と`PlayerAreaTracker`
- `OpenBetaSystems`の`NekolposGameBootstrap`と`ChatUIController`
- `PlayerTransitionPoints/PlayerSpawn_Table` を `Walk Start Destination`、`PlayerTransitionPoints/PlayerSpawn_Desk` を `Walk Start Desk Destination` として指定
- `PlayerCamera`と通常会話用の`Main CameraB`

通常会話中はPlayer SafetyとPlayerCamera／そのAudioListenerを無効にする。散歩中だけPlayer SafetyとPlayerCameraを有効にし、通常会話CameraとそのAudioListenerを無効にするため、AudioListenerは常に1つだけ有効になる。

移動先マーカーの既存WaypointUIManagerはPlayerCameraの有効状態を参照する。カメラ停止・非Active化・Manager停止時は表示済みマーカーをプールへ回収し、通常会話に残さない。対象登録は維持し、散歩カメラ復帰後に再表示する。停止判定は更新間隔とTimeScaleに依存しない。

使用中の`Assets/DL Assets/WaypointSystem/Demo/Marker.prefab`はアイコンのみで、距離テキストを持たない。未設定のDistanceTextUIは削除し、WaypointMarkerUIの任意参照も空にして生成時エラーを防ぐ。WaypointSystem配下はGit除外対象のため、パッケージ再導入時はこのPrefab修正とWaypointUIManagerのカメラ停止対応を維持する。

`WalkModeChanged` は状態変更が確定した時だけ通知する。`CatLocomotionController` はこの通知を正本として、散歩開始時だけ猫用NavMeshAgentを有効化し、終了通知では通常会話拠点へ戻す処理より先に移動所有権を解放する。

開始時に`PlayerAreaTracker.CurrentAreaId`が`Desk`ならDesk地点、それ以外はTable地点を使う。Desk地点が未設定の既存SceneはTable地点へフォールバックする。両方の開始地点が未保存の場合だけ、互換用フォールバックとして`PlayerTransitionPoints`配下の`PlayerSpawn_Table`／`PlayerSpawn_Desk`を一度だけ探索する。MainSceneではInspector参照を正とする。

## 復帰規則

`P`はWalkMode中だけ処理する。終了時は`PlayerAreaTracker.CurrentAreaId`が`Desk`なら`CatHomeLocation.Desk`、それ以外（`Table`、`None`、Bathroom、Bed等）は`CatHomeLocation.Table`を既存`NekolposGameBootstrap.SetHomeLocation`へ渡す。PlayerのAreaと猫の通常会話拠点は別状態として扱う。

## ポーズメニューとの連携

散歩中だけポーズメニューに「スタック脱出」を表示する。現在エリアがDeskなら既存のDesk散歩開始地点、それ以外はTable開始地点へ戻し、安全位置とAreaを更新してメニューを閉じる。散歩・猫の行動・会話モードは切り替えない。遷移中およびタブレット操作中は実行不可。脱出先が未設定の場合は警告して移動せず、メニューを維持する。

散歩中に`Esc`で既存の`OpenBetaPauseMenuController`を開くと、`PlayerWalkModeController`は`PlayerSafetyMovementController`を無効化する。これにより歩行とマウス視点回転の両方を停止し、カーソルを表示・解放してポーズメニューを操作できる。メニューを閉じると、散歩専用の別UIが開いていない場合だけ、入力とカーソルロックを復帰する。散歩カメラ自体と散歩状態は維持される。
