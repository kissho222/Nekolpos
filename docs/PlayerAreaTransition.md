# プレイヤーエリア遷移

`PlayerAreaTransitionController` は、通常歩行では到達しない場所へPlayerを安全に再配置する共通基盤である。ネルコによる運搬、充電ケーブル、特殊イベント、将来の背中への移動で使用する。猫のホーム位置（`CatHomeLocation`）は変更せず、Playerの現在地エリアだけを更新する。

## MainSceneの設定

1. `Player`に`PlayerAreaTransitionController`を追加する。`PlayerSafetyMovementController`と`PlayerAreaTracker`は同じGameObjectに必須であり、Controllerが自動取得する。
2. `Fade Controller`には、MainSceneで通常の画面フェードを担当する既存の`FadeController`をInspectorから明示的に割り当てる。文字列名検索や座標検索は行わない。
3. 移動先は既存の`PlayerAreaVolume`に収まるTransformを、運搬・ケーブル・イベント側で明示参照する。移動先Transformの自動生成は行わない。

## API

```csharp
// 既存の演出が完全暗転を所有している間だけ使う。二重フェードしない。
bool moved = playerAreaTransition.TeleportImmediately(destinationTransform, expectedAreaId: "Desk");

// このControllerが暗転・明転を所有する汎用遷移。
bool moved = await playerAreaTransition.TransitionToAsync(destinationTransform, expectedAreaId: "Floor");
```

どちらも移動中は`PlayerSafetyMovementController`を一時停止し、SafetyRecoveryの既存状態を保存する。移動には`TeleportTo(..., updateSafePosition: true)`を使うため、移動先が新しい`LastSafePosition`になる。直後に`PlayerAreaTracker.RefreshAreaNow()`を実行する。`expectedAreaId`を指定して不一致だった場合はWarningのみを出し、ロールバックや進行停止はしない。

`TransitionToAsync`は`FadeOutAsync()`の完了後、実際に不透明であることを確認してから移動する。暗転を確認できない場合はPlayerを動かさず、元の入力・SafetyRecovery状態へ復帰する。`IsTransitioning`で多重要求を検出し、後続要求は実行しない。
