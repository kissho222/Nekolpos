# 散歩専用会話

Issue #48の選択肢順は、上から追従切替（待ってて／ついてきて）、運んでほしい、ちょっと下がって、お散歩終わり！、なんでもない。その他の選択肢の相対順・有効条件・action IDと分岐処理は維持し、散歩終了を下から2番目、何もしない項目を最下段に置く。

`WalkDialogueController` は `Player` に置き、散歩中だけ一方通行の表示と選択肢を管理する。通常の `DialogueManager` と `ChatUIController` を呼ばない。

- データは `Assets/Resources/Dialogue/WalkDialogue.csv`。必須列は `TriggerType, TriggerId, Speaker, PsychologyState, Text, Order`。
- `Choice/NerukoTalk` は `NerukoTalkInteractable` の E 操作で使う。心理状態が未登録（Affectionを含む）の時は `NORMAL` 行にフォールバックする。
- `Interaction/<ID>` は `WalkDialogueObjectInteractable` または既存Interactorから再生する。ID、Prompt、1回限り、Cooldownは各コンポーネントのInspectorで設定する。空文はエントリ化しない。
- `Area/<AreaId>` は `PlayerAreaTracker.AreaChanged` から一度だけ再生を要求する。個別Colliderが必要な場合は `WalkDialogueTrigger` を追加し、IDとCooldownを設定する。
- ネルコ吹き出しは `Neruko Bubble Anchor` を優先し、未設定なら `Neruko Root Fallback` を使う。いずれもInspector参照で、画面外でも端へクランプする。
- ネルコの台詞はTMPの`maxVisibleCharacters`によるタイプライター表示。通常会話の`ChatUIController.typingSpeed`を参照し、未設定時は`Typing Seconds Per Character`（0.05秒）を使用する。Playerの観察文は従来どおり全文表示する。既存CSVを使用し、新しい台詞は生成しない。
- 発話中はクリック／タップ、Enter、Space、Eで操作する。文字送り中の最初の決定は全文表示だけ、次の決定で次行へ進む。同一フレームの連打は1回として扱い、行・選択肢が開いたフレームの入力を持ち越さない。自動送りの`Auto Hide Seconds`は全文表示後から計測する。ポーズ中は文字送り・自動送り・決定を停止し、散歩終了・無効化でコルーチンと残った入力を解除する。
- 選択肢は既存EventSystemとInputManagerのVertical/Submitで↑↓・W/S、Enter/Spaceを使用し、Eでも決定できる。無効な「運んでほしい」を飛ばし、初期選択を有効項目へ設定する。選択中はボタンの金色のSelected Colorで示す。Escは選択肢を閉じる操作を優先し、同じ押下でPauseが開かない。選択肢表示中は自動で閉じず、マウス／タップ操作も維持する。メニュー決定・キャンセルで使用したフレームのE/Spaceは散歩インタラクト・ジャンプへ流さない。
- 吹き出しは発話者ヘッダー、状態別アクセント色、輪郭、影を持つ。Playerの説明文はネルコ台詞と別配色・別ヘッダーで表示する。
- 「お散歩終わり！」は既存の`DialogueManager`時間経過イベントへ遷移する。`action_walk_result`、`action_walk_result2`、`action_walk_result3`のいずれかを毎回ランダムに1件だけ再生する。完全暗転を確認した後、Playerを最寄りのTable/Desk Anchorへ`PlayerAreaTransitionController.TeleportImmediately`で安全に移動し、その暗転中に通常会話カメラ・UIへ復帰する。時間進行・結果表示・明転・通常会話入力への復帰は既存イベントフローだけが担当するため、散歩側は時間を直接加算しない。

MainSceneではPlayerのControllerへ `Normal_Cat`、PlayerCamera、`DialogueManager`、`PlayerAreaTransitionController`、`CatLocomotionController`、Table/Deskの通常会話Anchor、TimeManager、StatusManagerを割り当てる。追従切り替えは1枠で、Follow中は`待ってて`、Wait中は`ついてきて`を表示する。選択後は閉じ、次に話しかけた際に現在の追従状態から表示を更新する。`運んでほしい` だけは未実装のまま無効にする。CableのコメントIDは `CableDesk` に設定済み。新しい家具等は `WalkDialogueObjectInteractable` を追加してCSVのIDと一致させる。

話しかける判定はInspectorで調整する。ネルコ専用の範囲はNormal_CatのBox Collider（Is Trigger）のSize/Center。PlayerのPlayer Interactor > Interaction Distance（現在0.12m）は届く距離、Sphere Radius（現在0.008m）は照準の許容幅で、すべてのInteractableに影響する。

「ちょっと下がって」を追加。`ChoiceResult/StepBack`の「ああごめん。ちょっと下がるね。」を表示し、発言終了後に`CatLocomotionController.TryStepBack()`を呼ぶ。伏せ終了→後退→3秒の追従休止を既存Controllerが担当する。追加の台詞生成は行わない。あくび中のインタラクトは1件だけ予約し、再生完了後に既存会話を開始する。散歩終了・優先演出による中断時は予約を破棄する。ボタンはクリック／タップで操作できる。

## 固定Viewと編集（Issue #49）

見た目の正本は`Assets/Prefab/WalkDialogueView.prefab`。MainSceneに配置し、PlayerのWalkDialogueControllerへCanvas、ネルコ／Player吹き出しとTMP本文、ChoicePanel、追従ラベル、上から順の5個のButton、文字送り速度元をInspectorで接続する。Runtimeで固定UIを新規生成せず、位置・サイズ・余白・フォントを無条件に設定し直さない。未接続Viewは明瞭なErrorを出し、通常の開始条件では会話を開始しない。

パネルは600×520、ボタン504×64・文字26、タイトル32。吹き出しは680×210、本文Auto Size22〜28、ヘッダー22。既存Canvasウィンドウと同じUIStyle.NormalPanel/NormalButton/NormalTextを使用し、淡いベージュ背景・濃い文字・金色の選択状態をPrefab上で設定する。Button Imageは白色としSelectableの色を二重乗算しない。下部のControlsHintは削除し、選択／決定／閉じる案内は右下ControlGuideHUDの文脈別行へ集約する。「クリック/タップでも操作」は表示しない。CanvasScalerは1920×1080・Match0.5、Sorting Order102。ネルコ吹き出しの追従位置だけはRuntimeで更新し、実際の吹き出し寸法を考慮して画面端へ収める。

Player／Controllerが無効になった場合は表示・文字送り・選択状態を解放する。Sceneが固定Canvasの寿命を所有し、Controller破棄時は自分が登録したButtonリスナーだけを解除する。SceneアンロードでViewが先に破棄されてもUnityのnull判定で参照を扱う。終了選択肢は従来どおり返答後に既存時間経過イベントへ渡し、暗転中の復帰・時間進行を重複して実行しない。

追加修正検証: 関連56/56＋通常選択肢表示条件1/1＋インタラクト可否1/1、合計58ケース成功（失敗0/Skip0）。HD/FHD/WQHD/4K/4:3/21:9で配置と文脈別ラベル収容を確認し、HD/4:3の合成描画を画像確認。実Player/実機操作とWindows再Buildは未実施。
