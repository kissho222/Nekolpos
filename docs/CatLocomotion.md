# ネルコ散歩移動基盤

## 散歩待機・睡眠・後退（2026-10-04）

伏せ待機中は40〜50秒のランダム間隔で`CatSimple_Lie_belly_loop_3`を1回再生する。歩行・会話／タブレット中は待機間隔をリセットし、あくび再生中は位置と伏せ姿勢の所有権を維持する。通常会話と同じ下方向視線制限65度を一時適用し、終了時に解除する。インタラクト要求は1件だけ予約し、normalizedTime>=1かつ遷移終了確認後に会話へ進む。完了確認失敗時は会話を開かず警告して伏せへ戻す。

時間経過による睡眠Start・Loop・起床End中はLookRigの外部Weightを0へフェードし、起床完了・中断・散歩終了で復帰させる。近距離の伏せ待機・Wait指定・到達不能待機のいずれも連続60秒で睡眠へ入る。あくび中も待機時間を加算し、再生を中断せず終了後に睡眠へ移行する。歩行再開・会話／タブレット開始・優先演出・散歩終了でタイマーをリセットする。

「ちょっと下がって」の返答終了後、`CatSimple_Lie_belly_end`の完了を待ち、`CatSimple_Walk_B_IP`を再生しながら現在の向きの後方へ移動する。既定距離は0.25m（`Step Back Distance`）、速度は既存`Move Speed`。NavMeshAgent.Raycastで通行禁止境界・端を検出して手前で停止し、Root Motionは使用しない。後退後は伏せへ戻り、3秒間追従を停止して元のFollow/Wait指定へ復帰する。散歩終了・優先演出でCoroutineを中断する。新規C#ファイルは追加しない。

`CatLocomotionController` は、散歩モード中だけ `Normal_Cat` の位置・回転と移動アニメーションを所有する。通常会話、Opening、Timeline、イベント中は所有権を持たず、既存の `CatPositionController`、`CatPresentationModeController`、Timeline を優先する。

## NavMesh 構成

- Unity AI Navigation `com.unity.ai.navigation` 2.0.15を使用する。
- `MainScene/CatNavigation` のFloor Surfaceと、配下のTable／Desk Surfaceは `Walkable` LayerだけをPhysics Colliderから収集する。小型ワールドの独立面を破棄しないよう `Min Region Area` は `0.01` とする。
- 複雑な家具MeshColliderは上面のベイクが不安定なため、Table／Desk Surfaceは天板内側に一致する `SurfaceProxy` をベイク入力として使う。Proxyは見た目を持たず、既存の家具当たり判定を置換しない。
- Agent Typeは猫専用の `Neruko`。半径、身長、段差、Voxel Sizeは巨大な猫と小型ワールドの比率に合わせて設定する。
- PlayerはNavMeshAgentを使用しないため、猫用NavMeshや猫専用立入禁止設定の影響を受けない。
- 猫専用立入禁止は `CatNoGoVolumes` 配下へ `NavMeshModifierVolume` を置き、Areaを `Not Walkable` にする。動的な障害物が必要な場合だけ `NavMeshObstacle` のCarvingを使う。

## MainScene 契約

`Normal_Cat` には、無効状態で待機する `NavMeshAgent` と `CatLocomotionController` を置く。Controllerは次をInspectorで明示参照する。

- `Normal_Cat` の `Animator`
- `Player` の `PlayerWalkModeController`、`PlayerAreaTracker`、Transform
- `CatPresentationController` の `CatPresentationModeController`
- `Normal_Cat/Arm_Cat` の `NekomataLookRigController`
- `CatAreaAnchors` 配下の `Table`、`Desk`、`Floor`

Area IDは大文字小文字を区別せず照合するが、Sceneでは `Table`、`Desk`、`Floor` に統一する。追従中のArea変更時に現在地からPlayerへ完全な経路がない場合だけ、対応Anchor近傍の猫用NavMeshへWarpする。待機中はPlayerが別Areaへ移動してもその場に残る。Anchor欠落やNavMesh欠落は例外にせず警告し、その場で待機する。

## 追従と停止

- `WalkDialogueController` の「付いてきて」は `FollowPlayer()`、「ここで待ってて」は `WaitHere()` を呼ぶ。「運んでほしい」は未実装のまま無効にする。
- 追従開始距離は停止距離より大きくし、ヒステリシスで境界付近の往復を防ぐ。
- 再経路探索は一定間隔かつPlayerが一定距離動いた時だけ行う。
- 目的地はPlayer位置そのものではなく、猫から見てPlayerの手前に停止距離を確保したNavMesh上の点とする。
- 移動中は `CatSimple_Walk_F_IP`、停止時は `CatSimple_Lie_belly_loop_1` を使う。現在の`CatLocomotionController`は既存の実装互換のため直接CrossFadeを維持しているが、新しい呼び出し元は下記の`CatMotionController.PlayLocomotion()`を利用できる。
- FollowStopDistance内ではNavMeshAgentのPathと速度を解除する。歩行停止時は`CatSimple_Lie_belly_start`→`CatSimple_Lie_belly_loop_1`へ遷移し、Wait中も伏せを維持する。散歩開始時も伏せ待機として扱う。追従再開は`CatSimple_Lie_belly_end`→`CatSimple_Walk_F_IP`の順で、立ち上がり終盤までAgentを停止する。Start/Endの残り時間が`Animation Blend Seconds`（既定0.15秒）になった時点で次のStateへCrossFadeし、終わり際を重ねる。旧Near Rest Delayの遅延は廃止した。停止中はbody/rootをPlayerへ向け直さず、既存LookRigだけが視線を維持する。Slow Walk、Turn In Place、Runは使わない。
- AgentのAngular Speedは既定・MainSceneとも120度/秒以下に抑える。曲線経路上の自然な回転だけを許可し、停止中にAgentで高速Yaw追従しない。左右・斜めのIn-Place Clipは素材にはあるが、現行ControllerにState登録されていないため使わない。
- Area Warp後は経路と速度をリセットし、視線Rigを再評価する。追従指定は維持される。

調整値と現在状態は `CatLocomotionController` のInspectorに表示され、Scene Viewでは開始・停止距離と現在目的地をGizmoで確認できる。

## Follow停止・休止復帰の修正（Issue #28）

MainSceneのController参照、Follow/Wait Choice参照、3つのSurfaceと猫用Agent Type、各Area AnchorのNavMesh到達性はテストで確認する。ユーザー指示により散歩開始時はFollowで開始する。「ここで待ってて」でWaitへ、「付いてきて」でFollowへ切り替える。再度の散歩開始はFollowへ戻す。

MainScene実行時のTable上で、猫の開始位置から到達できる最遠点は約0.665mだった。旧開始距離0.75mではTable内の歩行で追従を開始できないため、開始距離を既定・MainSceneとも0.55m、停止距離を0.45mへ設定する。Table/DeskのArea Volume上端は立位CharacterController中心を含むよう8cm拡張し、下端と水平範囲は維持する。Scene終了時は破棄済みPresentation参照をUnityのnull比較で除外する。

追従目的地は既に `stoppingDistance` 分だけPlayerの手前に置くため、Agent自身の停止距離は `max(0, followStopDistance - stoppingDistance)` とする。同じ距離をAgentへ再指定して二重加算しない。既定値では目的地オフセット0.4m＋Agent停止0.05mでPlayerから0.45mを保つ。

休止開始・終了Clipの遷移中は完了を待ち、起き上がりCoroutineを毎フレーム再開始しない。Locomotion所有中は停止・休止中もIdle Variationの競合を止め、演出への移行または散歩終了で所有権を返す。開始距離は `>= Start`、停止は `<= Stop`、中間帯は直前状態を維持する。

InspectorのRuntime Debugには有効状態、追従状態、移動状態、Player距離、NavMesh所属、経路有無・状態・残距離、現在Area、Animationを表示する。開始・停止距離はFollow Distancesで調整する。常時ログを追加しない。

## Animatorの棚卸しとLocomotion API

`Normal_Cat` は`NormalCatSimple_AnimContr 1.controller`を使用する。実際にStateとして登録されている移動Clipは、In-Place版の`CatSimple_Walk_F_IP`（前進）と`CatSimple_Walk_B_IP`（後退）、Idleの`CatSimple_Lie_belly_loop_1`／`_2`／`_3`、近距離の伏せ姿勢にも使う`CatSimple_Lie_belly_loop_1`、その開始／終了用の`CatSimple_Lie_belly_start`／`CatSimple_Lie_belly_end`である。LoopはLoop設定、StartとEndは非ループである。素材には`Walk_L_IP`／`Walk_R_IP`／斜め歩行、Run／RunFastのIn-Place Clipもあるが、このAnimator Controllerには未登録のため、Scene側でStateを追加するまで指定しない。`*_RM`はRoot Motion版の別素材であり、使用しない。

自由歩行の実座標はNavMeshAgentが所有するため、Animatorは常に`applyRootMotion = false`にする。`CatMotionController.PlayMotion()`は会話・Timeline向けで、従来どおり`CatPositionController.SnapToHome()`を行う。一方、`PlayLocomotion(stateName, blendSeconds)`はState存在を確認してからホームを一切動かさずに再生する。`StopLocomotionAndReturnToIdle()`も同じく位置を動かさずIdleへ戻す。公開状態は`IsPlayingLocomotion`と`CurrentLocomotionState`であり、`IsPlayingMotion`とは独立する。

優先度はConversation／Timeline MotionがLocomotionより上である。通常`PlayMotion()`が呼ばれるとLocomotion状態を解除する。会話Motionが再生中の`PlayLocomotion()`は要求を拒否する。API自身は`CatPresentationModeController`、Idle Variation、LookRigを操作しないため、接続側がPresentationの所有権を先に確認する。#23の接続では、散歩中だけIdle Variationを`SetLocomotionActive(true)`で止め、Timeline開始時・通常会話復帰時にはLocomotionを停止してから既存の`PlayMotion()`／`ResumeIdleAfterTimeline()`へ所有権を返す。

Smoke TestでScript再読込時に失われる非保存データも修正した。WalkDialogueはOnEnableでCatalogを再取得する。NekomataLookRigは初期化済みフラグだけ復元されconstraintsが欠落したRigLayerを新しいLayerへ置換してからBuildする。LayerのRigとactive設定は保持し、例外の抑制はしない。

Followの歩行Stateは空のBlend Treeを除き、既存FBXの実Clip `CatSimple_Walk_F_IP`をMotionに直接割り当てる。State名が正しくてもMotionが空なら歩行姿勢にならないため、テストではClip型と名称も検証する。

## 散歩の視線とタブレット待機

`PlayerWalkModeController.WalkCamera`のTransformを散歩中の注視先とする。会話用`PlayerLookTarget_AdjustHere`のMain CameraB参照・上下オフセット・固定Target Distanceを散歩へ持ち込まない。目の生成ターゲットとDebug_FinalNekomataLookAimTargetはPlayerCamera位置へ追従し、Idle Random Lookも散歩中は加算しない。首の生成ターゲットだけは既存Yaw/Pitch制限を適用し、首の制限で両目の注視点をずらさない。`GeneratedNekomataWalkHeadAimTarget`は実行時にRig配下で再利用する。通常会話・Editor Previewでは従来のターゲットと角度制限を使う。

タブレット開始時は`TabletInteractionChanged(true)`でAgentを同フレームに停止し、閲覧中はFollow/Wait指定を保持して現在位置で待つ。終了時に元の指定で追従を再開する。電源OFFによるDesk会話位置への移動はConversation入口だけに適用し、WalkMode入口では猫の配置を変更しない。

## 到達不能時の伏せ・睡眠・起床（Issue #35）

Followの経路を伏せから立ち上がる前に確認する。Invalid/Partial/目的地解決不可の間は、その場でCatSimple_Lie_belly_loop_1を維持し、既存Repath Intervalごとに再評価する。通常の失敗経路でArea AnchorへWarpするフォールバックは廃止し、明示的なArea移動時だけ既存Warpを使用する。待機開始は再計算間隔分の猶予と伏せ遷移完了後。連続60秒で睡眠へ移行する。

睡眠StartはCatSimple_Lie_belly_sleep_start、LoopはCatSimple_Lie_belly_sleep、起床StateはCatSimple_Lie_belly_sleep_End。End Stateへは既存In-Place素材CatSimple_Lie_belly_sleep_end（末尾小文字）を割り当てる。Start/Endはいずれも既存0.833秒の非ループClipで、新規Animationは作成しない。伏せStart/Endは従来の終盤重ねを維持するが、睡眠Start/起床EndはnormalizedTime>=1かつ遷移終了を確認し、完了前に次段階へ進めない。タイムアウト時も完了確認なしでUIを開かず、伏せへ復帰し警告を残す。

睡眠中は経路が戻っても自動起床せず、インタラクトまで維持する。NerukoTalkInteractableは睡眠時のみTryWakeForInteractionを呼び、起床中はIsInteractingとして二重入力を拒否する。PlayerのInteractionMenu状態を確保して起床完了を待ち、その後に既存WalkDialogueController.OpenWalkChoiceを呼ぶ。CSVの台詞や既存返答内容は変更・追加しない。

到達不能待機中に経路が戻ればタイマーを0へ戻し、既存belly_endからFollow再開。Interaction/Tabletへの移行、Wait指定、散歩終了、Timeline/Event/Conversation Motionへの所有権移行でもタイマーを解除する。起床中に優先イベントへ移行した場合はCoroutineとUI開始callbackを破棄し、InteractionMenu入力停止を解除する。睡眠や起床でもapplyRootMotion=false、座標所有はAgentのまま。

Runtime DebugのUnreachable Wait Seconds / Unreachable Stateで状態を確認できる。睡眠Start/End State追加は購入素材配下の既存Animator Controllerに対するローカル変更で、同フォルダはGit対象外。別環境では既存IP素材とStateの対応を再現する必要がある。
検証結果: Unity Test Runnerの9件一括＋追加2件（重複1、計10種類）Passed。短い失敗の姿勢猶予、60秒睡眠、経路復活、Conversation Motion/Timeline優先・Eventで起床UI中断を含む。MainScene実素材で起床中53framesはUIなし、完了後に既存返答UI、二重要求拒否・位置差0・RootMotion=false。最終Console Error0。人のE入力/描画最終確認とWindows再ビルドは未実施。日数/開始日Prefs復元済み、現在dirtyのMainSceneは未保存で保持。ログ/結果はC:/Users/shoho/.codex/tmp/issue35。

後退は真後ろ1本のRaycastで中止せず、真後ろから左右15/30/45/60/75度の扇形へstepBackDistance（既定0.25m）の候補を調べる。全距離を進める最も真後ろに近い候補を優先し、全候補が遮断される場合だけ最長の安全な短距離を選ぶ。現在のNavMeshAgentからのRaycastで進入不可領域を避け、移動中も毎フレーム確認する。向き・起き上がり完了→Walk_B_IP→後退完了後3秒の追従停止は維持する。

## 段差での後退停止と会話による中断（2026-10-04）

後退の到着判定は水平距離（2mm以内）を使用する。固定した目標高さとNavMesh上の段差による実際の高さを3D距離で比較し続けない。扇形候補は猫専用NavMeshへ投影し、水平位置のずれとRaycastで安全性を確認する。移動中は0.5秒間で水平2mm以上の進捗がない場合、または距離・速度から算出した移動期限（最低2秒）を超えた場合に後退を停止し、既存の伏せ・3秒待機へ戻す。時間停止中はこの時間を進めない。

後退の起き上がり・移動・3秒待機中もNerukoTalkInteractableの会話入口を使用できる。会話要求時に後退Coroutineを解除して停止し、既存の散歩Choiceへ入る。「散歩終了」は既存の終了処理を使用する。睡眠の起床・あくびの予約・会話中の二重入力制限は維持する。Area Warp成功時も進行中の後退・睡眠を解除し、Warp後の位置へ古いCoroutineが移動を続けない。スタック脱出ボタンの機能追加はユーザー訂正により行わない。

検証: 既存2件＋高さ差・実移動停止／会話入口の新2件をUnity Test Runnerで4/4成功、保存済みNavMesh-CatNavigationを使用した玄関／キッチン／洗濯機手前の3地点後退を追加1/1成功（Skip0）。MainSceneのNavMeshを直接読む調査でも手前の高さ約0.062m→0.02mを確認。Scene・Collider・NavMeshの変更は行っていない。実キャラクターの見た目、会話から散歩終了する手動操作、Windows再BuildとiOS/Android実機は未検証。

## 同じFloorへの再入場・通常追従復旧のWarp抑止（2026-10-05）

MainSceneのFloor AreaはBoundsの高さ約0.02〜0.12m。Playerが床領域を一時的に外れるとFloor→None→Floorが起こり得る。CatLocomotionControllerは直近のAnchor登録済みAreaを独立して保持し、Noneを挟んでも同じAreaへ戻っただけならArea Warpを行わない。別の登録済みAreaへ移動した場合の到達不能Warpは維持する。

通常追従中にAgentがNavMeshから外れた場合は現在地近傍だけを再探索し、失敗時はその場で待機する。散歩開始の初期位置解決と明示的なWarpToAreaは既存Anchorを使用できるが、通常追従の復旧ではArea初期Anchorへフォールバックしない。Sceneの床範囲・家具Collider・ベイクは変更しない。

検証: Floor→None→Floorで到達不能でもWarpなし、現在地遠方でAgent無効時に初期Anchorへ戻らず停止、明示的Warp成功の追加テスト1/1成功。既存通常追従/伏せからの追従再開2/2成功、Skip0。最終Console Error0/Play停止/dirty=false。カーテン・ベッド周辺での実キャラクターの手動再現とWindows再Buildは未実施。
