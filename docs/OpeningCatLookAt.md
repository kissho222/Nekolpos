# OP猫注視カメラ

`OBOP_MoveStart.playable` を再生する `OP_Cat B/Arm_Cat` の `PlayableDirector` には、実行時に `OpeningCatLookAtController` が追加される。開始元は `NekolposGameBootstrap` の Pattern B `Move Start Director` であり、Sceneへ別のCamera Trackや常設Componentを追加しない。

## 制御範囲

- 0.00〜0.35秒: `CameraEventPivot` を現在の向きからOP猫へ滑らかに向ける。
- 0.35〜5.55秒: `head` Bone（または `Opening Cat Focus Target`）を平滑化した位置へ追従する。
- 5.55〜5.83秒: LookAtの重みを0へ下げ、既存TimelineのPivot回転へ戻す。
- 4.70〜5.70秒: FOVを遠距離用値から通常値へ戻す。5.83秒の衝突より前に復帰完了する。
- 0.00〜5.83秒: 同じ注視Targetまでのカメラ距離を使い、`NekolposDepthOfFieldController` がGaussian DoFの開始・終了距離を追従させる。注視終了後は従来のOP猫DoF抑止ルールへ戻す。

`CameraEventPivot`の位置と、5.83秒以降の既存Timeline Camera Track、6.433〜7.300秒の`CameraShake`は変更しない。LookAtは`LateUpdate`で、そのフレームにTimelineが評価したPivot回転にだけ上乗せする。そのため、解除時はTimeline側の姿勢へ短時間でブレンドされる。

## Inspector調整

`MainScene/OpenBetaSystems/NekolposGameBootstrap` の **Opening Cat Look At** で以下を調整する。

- `Look At Full Weight Time`、`Look At Release Start Time`、`Look At Release End Time`: 視線開始・解除のタイミング。
- `Rotation Degrees Per Second` と `Target Position Smooth Time`: 視線・対象座標の追従感。
- `Distant Fov`、`Normal Fov`、FOVの開始／復帰時刻: 遠距離演出の画角。
- `Opening Cat Focus Target`: 任意の注視専用Transform。未設定ならOP猫階層の`head`を使う。頭部揺れはTarget Position Smooth Timeで抑制する。

`MainScene/NekolposDepthOfFieldController` の **Opening Focus** で以下を調整する。

- `Opening Focus Start Offset`: 注視Targetのすぐ後ろからぼかし始める余白。
- `Opening Focus Falloff Distance`: 最大ぼかしに到達するまでの距離。
- `Opening Focus Blur Strength`: OP中のぼかし強度。
- `Opening Focus Transition Seconds`: 猫の移動へのDoF範囲の追従速度。

既定値は遠距離FOV `48`、通常FOV `65`、LookAt解除 `5.55〜5.83秒`、FOV復帰 `4.70〜5.70秒`、OP DoFの開始余白 `0.15m`、ぼかし遷移 `1.5m`、追従時間 `0.2秒`である。
