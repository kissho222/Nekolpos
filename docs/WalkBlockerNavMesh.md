# 散歩Blockerと猫用NavMesh（Issue #34）

MainSceneのネルコAgentとCatNavigation / TableNavMesh / DeskNavMeshは同じAgent Type（ID -1372625422）。SurfaceはCollect Objects=Volume、Layer Mask=Walkableのみ、Use Geometry=Physics Colliders。Floor範囲はCenter=(-0.33,0.05,-0.30)、Size=(3,0.2,6.5)、猫Radius=0.14m/Height=0.45m。Table/Deskは別の高さにある。同じAgentのSurfaceだが、Floorの禁止地点を別Surfaceが覆う状態は確認しなかった。OffMeshLink/NavMeshLinkなし。

## 原因と修正

3つのModifier VolumeはDefaultレイヤーだった。AI NavigationのNavMeshSurface.AppendModifierVolumesはLayer Maskに含まれないVolumeを除外するため、CatNavigationのCollectSourcesには修正前ModifierBoxが0件だった。

さらにWalkBlocker_CatOnlyとネコ侵入禁止TVはCenter.y=1で、禁止Volumeの下端が床から約0.9m上に浮いていた。Affected Agents=All、Not Walkable、Active自体は正しい。

3つをWalkableへ変更し、CatOnly/TVのCenter.yを0へ修正した。BothのCenter/Collider、CatOnly/TVの無効Colliderは維持。猫用3 SurfaceをInspectorのBakeと同じNavMeshAssetManager経路で再Bakeし、Assets/Scenes/MainScene/NavMesh-*.assetとMainSceneの参照を保存した。Follow距離、ヒステリシス、移動コードは変更していない。

## 今後の配置・Bake

- BlockerのGameObjectレイヤーをWalkableにする。Modifier VolumeはActive、Not Walkable、猫のAgentをAffected Agentsへ含める。
- ModifierのCenter/Sizeを床面に重ねる。ColliderとModifierは別設定なので、Colliderだけ調整しても禁止Volumeは追従しない。
- CatOnlyのColliderは無効、BothのColliderは有効・非Trigger。レイヤー変更だけではPlayerの物理障害にならない。
- 位置・Size・Center・Layer・Affected Agentsを変更したら、影響するSurfaceをInspectorでBakeし、Sceneと生成NavMeshDataを保存する。実行中の配置だけでは保存済みNavMeshは変わらない。
- Runtime Build処理はゲームコードに存在しない。床/ベッドのMeshはRead/Write無効なので、Play中のBuildNavMesh検証はRuntimeNavMeshBuilderのread accessエラーを出した。製品SceneはEditor BakeしたDataを読み込む。モデルをRead/Write有効へ変更する必要はない。
- CatLocomotionControllerはAgent Type付きSamplePositionとNavMeshAgent.CalculatePathを使い、PathCompleteだけを採用する。初期配置とArea変更時にWarpはあるが、通常FollowはAgentの経路移動を使用する。

## 検証

修正前、Bothを挟む(-0.9,0.05,0.1)→(-0.9,0.05,1.0)はPathCompleteの2点直線、CatOnly中心はSamplePosition成功。修正後、同経路は6点で迂回し、CatOnly中心は近傍SamplePosition失敗。全3 VolumeがCollectSourcesへ含まれ、保存済みBakeで全3禁止中心に歩行可能面がないことを回帰Assertで確認。

MainScene Playの実Followでは、Bothの反対側にPlayerを置き、595フレーム観測、移動0.8615m、最大6 corner、Volume横断0。BothのPlayer用ColliderにRaycast命中、CatOnlyには命中しないことも確認。

到達不能のMainScene Runtime再Bake試験はread accessエラーとAgent再配置が発生したため合格に含めない。代わりに既存LiveFollowFixtureへNot Walkable ModifierBoxを追加した回帰テストで、迂回可能な場合と全面閉鎖の場合の実Follow挙動を確認する。最終テスト結果は以下へ追記する。
最終結果: Unity Test Runner 6/6 Passed。検証用NavMeshで迂回時のFollow横断なし・反対側へ到達、迂回不可時は不完全Path・移動停止・位置差<0.01m。既存Follow開始/停止・伏せ再開・MainScene契約・会話ボタンも成功。最終Console Error 0。MCP Jobはdomain reload後に残留したため、実際のTestResults.xmlを確認し、終了済みJobをclear_stuckで解除した。結果をC:/Users/shoho/.codex/tmp/issue34/TestResults.xmlへ保存。Windows再ビルドと人の入力通過は今回未実施。

## ちゃぶ台の円形天板と脚（2026-10-05）

MainSceneの`つくえ ダイニング-011 W90D90H36`は既にモデルと一致する非Convex/非Trigger MeshColliderを持つ。空中の四隅で立てた原因は、別配置の`CatNavigation/TableNavMesh/TableSurfaceProxy`に重なっていた0.9×0.05×0.9のBoxCollider。これを`Assets/Physics/ChabudaiTopCollider.asset`（半径0.4498929、厚さ0.05、64辺、130頂点/256三角形）を使用する非Convex/非Trigger MeshColliderへ置換した。高さはモデルの天板上下に一致する。既存本体MeshColliderは維持する。

4本の脚は実Meshの脚先座標から、中心X/Z=±0.28284276、Y=0.155、サイズ0.04×0.31×0.04のBoxColliderを本体の子`ChabudaiLeg_NN/NP/PN/PP`として配置する。全てWalkable・非Trigger。床から天板下までを覆い、既存本体の脚形状も維持する。

ネルコは物理Colliderを押して停止する仕組みではなくNavMeshAgentで経路移動するため、各脚にNot WalkableのNavMeshModifierVolume（0.04×0.35×0.04、中心Y=-0.02）を併設し、Floorの床面から禁止領域を除く。Floor/Tableの保存NavMeshをEditorの標準Bake経路で更新。Deskは変更しない。天板外周の落下と脚のCharacterController通過阻止、保存済みNavMeshの4脚迂回を既存テストで確認する。

`Area_Table`のTrigger、PlayerSpawn/TableのAnchor、TowerInteract/CableTravelの参照・判定、移動コード、落下防止設定を変更しない。誤操作落下防止が有効な通常歩行では正しい円周の端で止まる。落下が許可された状態または旧Boxの四隅にいる場合、Colliderが空中の足場を作らない。

TODO: 新しい猫3Dモデル納品後に体格・耳位置・移動Colliderを確認し、ちゃぶ台下での耳の天板めり込みを調整する。仮モデルに合わせた猫の体格・高さ・アニメーション変更は今回行わない。

検証結果: 正規Unity Test Runner関連32/32成功・失敗0/Skip0（2026-10-05 13:20:17Z〜13:20:24Z）。既存PlayerSafetyMovementTestsで実CharacterControllerの天板内4地点保持/旧四隅4地点の床への落下/全4脚の通過阻止を確認。CatLocomotionControllerTestsで保存NavMesh全4脚のPathComplete迂回・中心非歩行可能、実Playの既存追従Controllerによる1脚迂回と反対側到達を確認。既存PlayerAreaTransition/PlayerInteractionも成功。MainScene参照のPlayerSpawn_Table/猫Table Anchorが更新後NavMesh上で取得可能、Area/Table移動インタラクトの設定が変更前と一致。Windows再Build・人の入力による実プレイ・モバイル実機は未実施。
