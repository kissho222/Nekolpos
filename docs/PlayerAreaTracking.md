# プレイヤー現在地エリア判定

`PlayerAreaTracker` は、プレイヤーがScene上のどの`PlayerAreaVolume`にいるかを判定し、会話・ワープなどが参照する現在地IDを提供する。安全歩行を担当する`PlayerSafetyMovementController`とは独立しており、移動・Rigidbody・Walkable判定を変更しない。

## Sceneへの設定

1. 本番Playerのルートに`PlayerAreaTracker`を追加する。`CharacterController`が同じルートにある場合は自動で判定位置に使用する。
2. 空のGameObjectへ`BoxCollider`などのColliderと`PlayerAreaVolume`を追加する。Colliderは`PlayerAreaVolume`によりTriggerへ設定される。
3. `Area Id`へ`Desk`、`Table`、`Bathroom`、`Bed`などを設定する。これらは予約済みの列挙値ではないため、将来のIDも追加できる。`None`は未所属を表す予約値なのでArea Idに使わない。
4. 領域が重なる場合は、優先させたいVolumeの`Priority`を大きくする。同値なら`Resolve Order`が大きい方を優先し、さらに同値なら`Area Id`の文字列順で決定する。
5. `MainScene > NekolposGameBootstrap` の `Player Area Tracker` に、本番Player上のTrackerを割り当てる。Bootstrapが`H:`の評価用Trackerとして明示的に登録する。

現在のワークスペースでは安全移動を持つPlayerが`MainScene`に保存され、`GameScene`にはPlayerがシリアライズされていない。対象Sceneが確定するまでPlayerやAreaVolumeを自動追加しない。各領域は実際のプレイヤー導線とScene縮尺を確認して配置する。

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

## 会話Condition `H:` との接続

`MainScene`では、`H:`の正本はBootstrapが登録した`PlayerAreaTracker.CurrentAreaId`である。`H: Desk`、`H: Desk Table Bathroom`、`H: !Desk`といった既存CSV構文はそのまま使える。`None`では正のID条件は不成立、否定ID条件は成立する。未定義IDはWarningを出して不成立になる。

`CatPositionController.CurrentConversationLocation`は猫の基本会話拠点・猫/カメラ配置の正本として残る。PlayerAreaTrackerを持たない旧テストSceneだけ、互換用として猫ホーム位置を`H:`の判定元にする。MainSceneではTrackerを見つけられない場合に猫ホーム位置へ黙って戻さないよう、Bootstrapの参照を設定する。
