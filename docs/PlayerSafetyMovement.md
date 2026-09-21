# プレイヤー安全移動

`PlayerSafetyMovementController` は、`CharacterController` を使用した通常プレイ専用のFPS移動と、正規の足場からの誤操作落下防止を担当する。写真撮影用の`FreeCameraController`とは独立しており、通常プレイはこのコンポーネントを使用する。

## Sceneへの設定

> 現在のワークスペースでは、`Player`・`PlayerCamera`・`PlayerSpawn_Floor`と`PlayerSafetyMovementController`は`TitleScene`に保存されている。`GameScene`には同じ構成がシリアライズされていないため、本番の対象Sceneを確定するまでは自動で複製・移動しない。

1. プレイヤーのルートGameObjectに `CharacterController` と `PlayerSafetyMovementController` を追加する。カメラをルートにする場合は、カメラを子にして目線の高さへ調整する。
2. `Walkable Layers` は初期状態では未設定である。歩いてよい床・机上・家具上だけを含む `Walkable` LayerをInspectorで指定する。壁面や家具側面を同じLayerに入れない。Layer名はコードへ固定していない。
3. 対象Sceneの床`1Room(軽量版)/床 フローリング/部屋床-001`は、既存のMesh Colliderを追加Colliderなしで使う。Layerを`Walkable`に設定する。既存の `Default` を暫定的に使う場合でも、壁や危険面は別Layerに分ける。`Walkable` Layer自体は`ProjectSettings/TagManager.asset`のindex 7に定義済みである。
4. 小さなプレイヤーの標準値は高さ `0.05`、半径 `0.01`、`center.y` `0.025`、`stepOffset` `0.01`。カメラを子にする場合の目線高は `0.043〜0.045` を初期目安にする。モデル・Scene縮尺に合わせてCharacterControllerと、`Ground Probe` / `Edge Protection` の各距離を調整する。

## 挙動

- 足元のSphereCastは `Walkable Layers` と最大傾斜で足場を判定する。側面のように上向き法線が不足するColliderは足場にならない。
- 移動前に進行方向の足場を確認し、`Maximum Safe Step Down` またはCharacterControllerの`stepOffset`を超えて下がる崖では水平移動を止める。
- 正規の足場へ安定して接地した後だけ、現在位置を`LastSafePosition`として記録する。異常落下では、`Recovery Fall Distance`または`Recovery World Y`を超えた時点で復帰する。
- Inspectorの`Runtime Debug`と選択時Gizmoで、Walkable接地、最後の安全位置、端で止まった状態、保護の有効状態を確認できる。

## 公開API

```csharp
playerSafety.SetMovementInput(moveVector);
playerSafety.ClearMovementInputOverride();
playerSafety.SetEdgeFallPreventionEnabled(false);
playerSafety.SuspendSafetyRecovery();
playerSafety.TeleportTo(destination, updateSafePosition: true);
playerSafety.ResumeSafetyRecovery();
playerSafety.SetLastSafePosition(position);
playerSafety.ResetLastSafePosition();
```

`SetEdgeFallPreventionEnabled(false)` の後は、イベント終了時に必ず `true` へ戻す。通常の場所移動、ネルコ追従、搭乗、現在地Condition、ケーブル移動はこのコンポーネントの対象外である。
