# プレイヤー安全移動

ネルコ本体への侵入防止は、既存CharacterControllerの物理接触に加え `PlayerCatBodyCollisionController` がカメラと身体の重なりを解消する。骨追従Collider・Layer・体型調整・検証結果は [NerukoBodyCollision.md](NerukoBodyCollision.md) を参照する。

`PlayerSafetyMovementController` は、`CharacterController` を使用した通常プレイ専用のFPS移動と、正規の足場からの誤操作落下防止を担当する。写真撮影用の`FreeCameraController`とは独立しており、通常プレイはこのコンポーネントを使用する。

## Sceneへの設定

> 現在のワークスペースでは、`Player`・`PlayerCamera`・`PlayerSpawn_Floor`と`PlayerSafetyMovementController`は`MainScene`に保存されている。`GameScene`には同じ構成がシリアライズされていないため、本番の対象Sceneを確定するまでは自動で複製・移動しない。

1. プレイヤーのルートGameObjectに `CharacterController` と `PlayerSafetyMovementController` を追加する。カメラをルートにする場合は、カメラを子にして目線の高さへ調整する。
2. `Walkable Layers` は初期状態では未設定である。歩いてよい床・机上・家具上だけを含む `Walkable` LayerをInspectorで指定する。壁面や家具側面を同じLayerに入れない。Layer名はコードへ固定していない。
3. 対象Sceneの床`1Room(軽量版)/床 フローリング/部屋床-001`は、既存のMesh Colliderを追加Colliderなしで使う。Layerを`Walkable`に設定する。既存の `Default` を暫定的に使う場合でも、壁や危険面は別Layerに分ける。`Walkable` Layer自体は`ProjectSettings/TagManager.asset`のindex 7に定義済みである。
4. 小さなプレイヤーの標準値は高さ `0.05`、半径 `0.01`、`center.y` `0.025`、`stepOffset` `0.01`。カメラを子にする場合の目線高は `0.043〜0.045` を初期目安にする。モデル・Scene縮尺に合わせてCharacterControllerと、`Ground Probe` / `Edge Protection` の各距離を調整する。

### 散歩中の壁カメラ補正

`MainScene/Player/PlayerCamera` には `PlayerCameraWallCollisionController` を追加し、`Wall Root` に既存の `1Room(軽量版)/壁-001` を設定する。これは家の壁MeshColliderだけを対象にする設定であり、ネルコ・家具・小物・Triggerは登録しない。

- `Probe Radius`（現在 `0.003m`）は、壁を手前で検出する球の半径。
- `Wall Clearance`（現在 `0.016m`）は、カメラ中心から壁まで残す安全距離。`PlayerCamera` のNear Clip Plane（`0.01m`）よりわずかに大きくし、Near Clipの変更でごまかさない。
- `Collision Smooth Time`（現在 `0.015秒`）と`Return Smooth Time`（現在 `0.06秒`）で、壁への接近と壁から離れた後の復帰を滑らかにする。

Controllerは散歩中に`PlayerCamera`が有効な時だけ、8方向の小さなSphereCastで壁Root配下だけを調べ、必要な表示位置補正をLateUpdateで加える。通常会話・OP・タブレットでは`PlayerCamera`が無効のため即座に元のローカル位置へ戻り、`Main CameraB`や`CameraEventPivot`を変更しない。

## 挙動

- 足元のSphereCastは `Walkable Layers` と最大傾斜で足場を判定する。側面のように上向き法線が不足するColliderは足場にならない。
- 移動前に進行方向の足場を確認する。足元が途切れても、下方にWalkable面があり、法線が歩行可能で、落差が`Maximum Safe Step Down`（MainSceneは`0.03m`）以内なら通常歩行のまま降りられる。下方に足場がない、または落差がこの値を超える崖では水平移動を止める。
- 正規の足場へ安定して接地した後だけ、現在位置を`LastSafePosition`として記録する。異常落下では、`Recovery Fall Distance`または`Recovery World Y`を超えた時点で復帰する。
- 空中での落下速度はInspectorの`Max Fall Speed`（初期値 `2 m/s`）を上限とする。重力は従来どおり `9.81` のままで、接地時の`Grounded Downward Speed`にはこの上限を適用しない。
- 散歩中は暫定キー`Space`で単発ジャンプできる。`Jump Height`はMainSceneで`0.1m`で、初速は`sqrt(2 * gravity * jumpHeight)`から計算する。Walkable接地中だけ開始でき、空中再ジャンプはできない。上昇中も、ジャンプ高さと安全な段差範囲にWalkable着地点がある場合だけ水平移動を許可するため、虚空への飛び出しは従来どおり防止する。
- Inspectorの`Runtime Debug`と選択時Gizmoで、Walkable接地、最後の安全位置、端で止まった状態、保護の有効状態を確認できる。

## 公開API

```csharp
playerSafety.SetMovementInput(moveVector);
playerSafety.ClearMovementInputOverride();
playerSafety.SetEdgeFallPreventionEnabled(false);
playerSafety.SuspendSafetyRecovery();
playerSafety.TeleportTo(destination, updateSafePosition: true);
playerSafety.SetLookRotation(returnRotation);
playerSafety.ResumeSafetyRecovery();
playerSafety.SetLastSafePosition(position);
playerSafety.ResetLastSafePosition();
```

`SetEdgeFallPreventionEnabled(false)` の後は、イベント終了時に必ず `true` へ戻す。通常の場所移動、ネルコ追従、搭乗、現在地Condition、ケーブル移動はこのコンポーネントの対象外である。
