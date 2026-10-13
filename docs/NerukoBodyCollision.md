# ネルコ本体の侵入防止（Issue #37）

## 構成

`MainScene` の `Normal_Cat` と `OP_Cat B` に、それぞれ22個（Capsule 18個、Box 4個）の非Trigger Colliderを配置する。共有Prefab、メッシュ、Avatar、Animator Controller、Timeline、視線設定は変更しない。写真撮影用の自由カメラには適用しない。

Colliderは各猫の `PlayerBodyBlockers` にまとめる。現在のモデルでは `spine_02` が非Activeで、頭・胴体・前脚の骨も非Activeになる。骨の子にColliderを置くと当たり判定まで無効になるため、骨のActive状態を維持し、`CatBodyColliderFollower` が対応する骨の位置・回転・Scaleを専用Transformへコピーする。実行順は視線Rig（10000）→追従（10500）→Playerの侵入解消（11000）。ColliderのCenterは骨座標で調整する。

身体ColliderにはRigidbody、Interaction Component、TabletWakeSourceを追加しない。尻尾、耳、髭の当たり判定は追加しない。RearWaistは腰の後端を覆う `spine_01` 用であり、尻尾の長さを覆うものではない。

## Layer

- Layer 8：`CatBodyBlocker`。Layer 9との接触だけを有効にする。身体同士・家具・床・猫用判定とは接触しない。
- Layer 9：`PlayerBody`。Playerルートだけを移す。既存Defaultと同じ接触関係を維持し、身体との接触を追加する。
- PlayerInteractorの既存検出Maskから `CatBodyBlocker` だけを除外する。会話用Box Triggerは従来のDefaultのまま。
- NavMesh SurfaceのWalkable Mask（Layer 7）には含めず、NavMeshの再Bakeは行わない。

`DynamicsManager.asset` はUnity 6000.3のSerializedObjectから編集・保存したため、Unityの現行シリアライズ形式にも更新される。

## Collider一覧

名前には `BodyBlocker_` 接頭辞を付ける。同じ設定を通常猫・OP猫へ使用する。単位はm、軸は骨のローカル座標。

| 部位名 | 追従骨 | 形状・寸法 | Center |
|---|---|---|---|
| Head | head | Capsule X、Radius .052、Height .134 | (0, .030, .018) |
| Muzzle | head | Capsule X、Radius .033、Height .080 | (0, .070, .008) |
| Neck | neck | Capsule Y、Radius .055、Height .110 | (0, .031, -.016) |
| Shoulders | spine_05 | Capsule Z、Radius .060、Height .170 | (0, .036, -.031) |
| Chest | spine_04 | Capsule Z、Radius .066、Height .165 | (0, .043, -.034) |
| Abdomen | spine_03 | Capsule Z、Radius .063、Height .160 | (0, .042, -.032) |
| Waist | spine_02 | Capsule Z、Radius .058、Height .145 | (0, .022, -.025) |
| RearWaist | spine_01 | Capsule Y、Radius .029、Height .100 | (0, .030, 0) |
| ScapulaL / ScapulaR | shoulder_blade.L / .R | Box (.036, .089, .080) | (±.005, .030, -.010) |
| FrontLefthip / FrontRighthip | hip_f.L / .R | Capsule Y、Radius .031、Height .118 | (0, .059, .004) |
| FrontLeftleg / FrontRightleg | leg_f.L / .R | Capsule Y、Radius .021、Height .120 | (0, .043, -.002) |
| FrontLeftfoot / FrontRightfoot | foot_f.L / .R | Capsule Y、Radius .017、Height .055 | (0, .018, -.002) |
| HindLefthip | hip_b.L | Box (.042, .150, .095) | (-.005, .046, -.015) |
| HindRighthip | hip_b.R | Box (.042, .150, .095) | (.005, .049, -.015) |
| HindLeftleg / HindRightleg | leg_b.L / .R | Capsule Y、Radius .021、Height .140 | (0, .056, -.002) |
| HindLeftfoot / HindRightfoot | foot_b.L / .R | Capsule Y、Radius .017、Height .095 | (0, .033, -.002) |

## Playerとカメラ

新規 `PlayerCatBodyCollisionController` は既存CharacterControllerの通常移動後、身体と近接描画面の重なりを解消する。PlayerCameraが有効かつCharacterControllerが有効な間だけ動作する。通常会話・OP・タブレットなどでPlayerCameraが無効な間はPlayerを補正しない。既存の壁用カメラ補正も維持する。

カメラ保護球はNear Clip Planeの四隅までの距離＋`Camera Surface Allowance`（.020）＋`Separation Margin`（.001）。Near Clip .010、FOV 60、16:9では約.03645m。Allowanceは単純形状と曲面メッシュの差を補う値で、視点がメッシュ内部を覗かないよう実メッシュとの照合で調整した。Near Clip Plane自体は変更しない。体型・カメラ設定変更時には再検証する。

重なりは最大12回、`CharacterController.Move` で押し戻す。床・壁の衝突を無視してTransformを移動しない。解消できない場合だけ、直近64回の確認済み位置、または既存安全移動Controllerの安全位置から、現時点で身体・床・壁・家具とカメラが重ならない復帰先を探す。復帰先がなければ `HasUnresolvedOverlap` と一度の警告で検出する。散歩再開や.5mを超える正規移動では履歴をリセットする。

重なり計算用には全Layerを除外したTriggerのCapsule/Sphereを1組だけ生成する。使用中のUnityでは無効Colliderの `ComputePenetration` がfalseになるため、これらは有効なクエリ専用形状にする。物理接触・点灯Trigger・Interactionには参加せず、Sceneには保存しない。CharacterControllerのカプセル軸は視点Pitchに関係なく鉛直に扱う。

## 体型差分の調整

`PlayerBodyBlockers` の各ColliderのCenter・Radius・Height・Sizeを変更する。追従先を変える場合は猫RootのFollowerのBindingsを更新する。既存骨を有効化したり、SkinnedMeshへMeshColliderを追加したりする必要はない。追加・削除時はPlayerのBody Colliders配列も更新する。

非一様Scaleと回転を組み合わせた階層のShearは単純Colliderで忠実に表現できない。現行のScale 1構成を基準とし、体型変更ではCollider寸法を調整する。Player側も現行Scale 1を前提とする。

## 検証

- 追加Editorテスト：非Active骨の追従、近接描画面の保護半径、身体が静止Playerへ重なる場合、カメラだけが先行する場合、遠距離非干渉、無効カメラ・Trigger除外、骨回転、壁に挟まれた際の安全復帰、Pitch時の鉛直カプセル、保存SceneのLayer・参照分離。
- Unity Test Runnerで追加10ケースと既存の安全移動・Interaction・散歩・タブレット復帰を合わせ50/50成功、Skip 0。MCPのジョブ状態はDomain Reloadを跨いでrunningのままになったが、同じ実行時刻のUnity生成TestResults.xmlとEditor.logで完了を確認した。直接NUnit呼出ではLogAssertのScopeがない既存1ケースを判定できなかったため、正規Runnerで再実行して成功を確認した。
- 実メッシュを一時的にBakeし、待機・あくび・前進の3Clipの中間姿勢で22部位×4方向＝264ケースを確認。全ケースでColliderの重なり解消成功。カメラ中心と近接描画面の四隅を三角形の立体角（winding number）で照合し、必須の身体部位への侵入0。
- メッシュ全体では3ケースで任意対象の尻尾との重なりが残る。最寄り三角形の主Boneはtail_03/04で、身体侵入ではない。尻尾を遮らない今回の範囲を維持する。
- 通常猫13Clip、OP猫101Clipを5時点ずつ（計570姿勢）サンプルし、22箇所の追従位置誤差は最大約2.8e-6m。
- Windows Release条件のPlayer用39アセンブリをRoslynで条件コンパイルし、Error 0。Unityの標準Warning除外を適用しない補助コンパイラでは既存の参照統合・Packageフィールド等のWarning 1438件（CS1701等）が出るが、追加2つのRuntimeソースのWarningは0。Playerの実Buildは実施していない。
- Unity Console Error 0。既存テストFixtureがBootstrapを生成する際のOnValidate/SendMessage Warningは残り、今回のRuntime ColliderのErrorではない。
- 検証用スクリプト・Collider一覧・結果JSONは `C:/Users/shoho/.codex/tmp/issue37-cat-body-20261004`。Bakeは検証用の一時Meshだけで、RuntimeのSkinnedMesh MeshColliderは使用しない。
- 目視による接触感・押し戻される距離の最終判断、全Timelineの通し再生、PC/iOS/Android実機はユーザー確認が必要。
