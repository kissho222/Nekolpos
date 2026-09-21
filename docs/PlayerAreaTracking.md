# プレイヤー現在地エリア判定

`PlayerAreaTracker` は、プレイヤーがScene上のどの`PlayerAreaVolume`にいるかを判定し、会話・ワープなどの将来機能が参照できる現在地IDを提供する。安全歩行を担当する`PlayerSafetyMovementController`とは独立しており、移動・Rigidbody・Walkable判定を変更しない。

## GameSceneへの設定

1. 本番Playerのルートに`PlayerAreaTracker`を追加する。`CharacterController`が同じルートにある場合は自動で判定位置に使用する。
2. 空のGameObjectへ`BoxCollider`などのColliderと`PlayerAreaVolume`を追加する。Colliderは`PlayerAreaVolume`によりTriggerへ設定される。
3. `Area Id`へ`Desk`、`Table`、`Bathroom`、`Bed`などを設定する。これらは予約済みの列挙値ではないため、将来のIDも追加できる。`None`は未所属を表す予約値なのでArea Idに使わない。
4. 領域が重なる場合は、優先させたいVolumeの`Priority`を大きくする。同値なら`Resolve Order`が大きい方を優先し、さらに同値なら`Area Id`の文字列順で決定する。

現在のワークスペースでは安全移動を持つPlayerが`TitleScene`に保存され、`GameScene`にはPlayerがシリアライズされていない。対象Sceneが確定するまでPlayerやAreaVolumeを自動追加しない。各領域は実際のプレイヤー導線とScene縮尺を確認して配置する。

## 判定とデバッグ

- TrackerはCharacterControllerの中心（未配置時はTransform位置）を使い、登録済みの有効なAreaVolumeを毎フレーム再評価する。TriggerのEnter/Exitも即時再評価の補助に使うが、Trigger通知だけには依存しない。
- 範囲外では`CurrentAreaId`は`None`、`CurrentArea`は`null`になる。同じArea Idが継続する間はイベントを再発行しない。
- PlayerAreaTrackerの`Runtime Debug`には現在ID、選択中Volume、候補Volumeが表示される。選択したPlayerAreaVolumeはBoundsのGizmoで表示する。

## 公開API

```csharp
string areaId = playerAreaTracker.CurrentAreaId;
bool isAtDesk = playerAreaTracker.IsInArea("Desk");
playerAreaTracker.RefreshAreaNow();

playerAreaTracker.AreaChanged += (previousAreaId, currentAreaId) =>
{
    // 将来の会話・ワープ側で利用する。
};
```

今回の対象は現在地判定と参照APIだけであり、Dialogue Conditionへの接続、ケーブル移動、ネルコ搭乗・追従、Scene全体への領域自動配置は行わない。
