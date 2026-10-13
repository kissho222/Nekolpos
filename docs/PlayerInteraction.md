# 散歩モードのワールドインタラクト

Issue #17では、`PlayerInteractor` が散歩モード中だけ `PlayerCamera` の画面中央から短距離のSphereCastを行い、検出した `IPlayerInteractable` に暫定キー `E` を渡す。検出距離（既定 `0.12m`）、半径（既定 `0.008m`）、LayerMask は `PlayerInteractor` のInspectorで調整する。Trigger Colliderも検出対象であり、入力欄が選択中・会話中・散歩メニュー表示中には実行しない。

## 共通構成

- Playerへ `PlayerInteractor` を追加し、`PlayerWalkModeController` と `PlayerCamera` を参照する。Issue #39からPromptは右下の共通操作説明HUDへ統合。旧中央Prompt Canvasは生成しない。HUDはWalk Controllerが実行時に初期化するためScene UIの追加は不要（`docs/ControlGuideHUD.md`）。
- 反応点にはTriggerのColliderと、`PlayerInteractable` 派生Componentを同じGameObjectまたは親に置く。既存のCollider・Layerを変更する必要はない。
- `PlayerInteractable > Prompt Text` に画面表示する文言を設定する。例: `E：話す`、`E：コードを渡る`。

## ネルコの反応点

ネルコの顔または前足の近くに専用の小さなTrigger Colliderを置き、`NerukoTalkInteractable` を追加する。`E` は通常会話を開始・再開せず、PlayerCameraを保ったまま散歩専用のコンテキストメニューを開く。メニュー表示中は移動と視点操作を停止し、カーソルをUIへ渡す。

初期メニューの `付いてきて` は `CatLocomotionController.FollowPlayer`、`ここで待ってて` は `WaitHere` を実行して散歩へ復帰する。`運んでほしい` は未実装として無効表示を維持する。`何もしない` は追従状態を変更せず、同じ位置・Areaのまま散歩へ復帰する。

## 電源コードの昇降点

MainSceneでは `CableInteract_Floor` と `CableInteract_Desk` を作成済みで、それぞれに専用の小さなTrigger Colliderと `CableTravelInteractable` を設定している。各Componentの設定は次のとおり。

| 反応点 | Destination | Expected Area Id | Prompt Text |
| --- | --- | --- | --- |
| 床側 | Desk側Transform | `Desk` | `E：コードを登る` |
| Desk側 | 床側Transform | `Floor` | `E：コードを降りる` |

`CableTravelInteractable` は直接座標を代入せず、Player上の既存 `PlayerAreaTransitionController.TransitionToAsync` を使う。これにより暗転中の安全な再配置、LastSafePosition更新、Area再判定、散歩モードの維持を一貫して行う。

## MainSceneの残作業

この機能のScene設定は `MainScene` のみが対象である。Playerへの`PlayerInteractor`、`Normal_Cat`への`NerukoTalkInteractable`と専用Trigger Collider、電源コード両端の反応点・Destination・Area Idを保存済みである。散歩開始からネルコメニュー、床⇔テーブルの往復、`何もしない`で位置とAreaが変わらないことは実機確認が残る。
## タイトル復帰・Scene離脱時の案内解除（Issue #27）

`PlayerInteractor.ResetInteractionState()` は対象参照を解除し、案内テキストを空にして非表示にする。散歩終了通知、InteractorのDisable/Destroy、Active Scene変更で呼ばれる。Pause中も検出を停止し、Resume後に再検出する。タイトル復帰はSceneロードより前に全Scene内Interactorをリセットして無効化する。

案内Canvasは生成元Playerと同じSceneに属し、`DontDestroyOnLoad`にしない。Playerの破棄時にもCanvasを破棄するため、タイトルや再開後に古いCanvas・対象参照が残らない。

