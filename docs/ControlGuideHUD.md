# 操作説明HUD（Issue #39）

本番対象は現在のMainScene。GameSceneは削除済みのため復元しない。
PlayerWalkModeController.AwakeがPlayerへControlGuideHUDを一度だけ追加し、既存のWalk ControllerとChatUIを明示的に渡す。Scene/Prefabへの手動追加は不要。

ControlGuideHUDはNone / Conversation / Walk / WalkChoice / WalkDialogueを持つ。ConversationはChatUIの通常入力欄が有効・操作可能で、文字送り/メニュー/抑制中でない時のみ。Dialogueモードの教示入力と、DialogueEngineへ入力を渡さないタイトル呼びかけ入力は除外する。Walkは既存散歩状態を参照し、遷移・Pause・タブレット中はNone。散歩選択肢中はWalkChoice（↑↓選択、Enter/Space決定、Esc閉じる）、散歩本文中はWalkDialogue（Enter/Space全文表示／次へ）だけを表示し、移動・ジャンプ・Eの案内を同時表示しない。既存CatPresentationModeのOpening/Timeline/Event、暗転中とTime.timeScaleが0の間もNone。参照を初期化時に保持し、毎フレームのScene探索は行わない。

ControlGuideEntry配列でモード・識別子・キー名配列・説明・表示可否を設定する。操作追加は項目追加で行い、未使用モードは実装しない。既存UIStyleの角丸SpriteをキーキャップImageへ使用し、キー文字と説明を別要素として描画する。背景は半透明、右下Anchor/Pivot、右余白28・下余白24、縦方向。CanvasScalerは1920×1080基準で幅/高さ0.5。全GraphicのRaycastを無効化し、入力を遮らない。

## ログ送信との配置・拡大（Issue #47）

操作説明は幅300、行高72、説明文字25、キー文字22。内側余白は左右18/上下16、行間10。各行は上段のキーキャップ（高さ34）と下段の説明（高さ38）を縦に並べ、キー幅は1文字36・複数文字88、キー間5とする。右下(-28,24)を維持し、中央入力欄をずらさずに4:3でも右端へ収める。ChatInputFieldはユーザー調整のbottom center/PosX0、PosY42、幅665/高さ54を正本とする。ユーザー指定の画面比率対応として、ChatUIControllerが表示領域端と表示中の操作説明から12単位の余白を確保できる幅に限り横幅を縮める。中心位置・高さ・Anchorは変更せず、広くなれば正本の幅へ復帰する。無効化時も正本の幅へ戻す。HUDはBind/再有効化で既存ChatUIへ参照を渡し、Scene検索は追加しない。会話欄の横Anchor0.04〜0.70と横sizeDelta0は維持する。可変項目の既存生成と表示条件・入力非遮断・参照解放は維持し、新しいUI系統は追加しない。

履歴／データ送信入口はMainSceneの`OpenBetaCanvas/DialogueUI/LogButton`を正本として編集する。1280×720基準で160×100（旧131.78×84.6）、文字26（旧22）、Anchor(1,0)/Pivot(1,0)、位置(-24,210)。右下の操作説明の上へ配置。通常会話の選択肢を開いている間は履歴入口を隠し、重なりを防ぐ。Runtimeで位置・サイズを上書きしない。ログ選択・確認画面・送信処理・表示条件は変更しない。

HD/FHD/WQHD/4K、1024×768、2560×1080相当のCanvasで、Conversation/Walkの両モードの操作説明とログ入口、会話欄、入力欄、履歴が操作説明の上にあること、画面内収容、ラベル収容を既存`ControlGuideHUDTests`へ追加した6件で確認する。通常会話の選択肢中は履歴入口と通常入力の案内を表示しない。Preview SceneではCanvasが未描画でScale0となるため、テストだけWorldSpace/Scale1で基準解像度に応じた矩形を計測する。本番CanvasScalerやSceneをテストから変更しない。

PlayerInteractionPromptは既存InteractorからPromptTextを受け取る互換窓口となり、旧中央Canvasは生成しない。E：/E:の先頭だけ除いてHUDの説明へ渡し、散歩中のE行は移動・ジャンプより上に配置し、既存Interactorが操作可能な対象のPromptを渡した時だけ表示する。対象解除時はE行を非表示にし、移動・ジャンプ案内は維持する。対象検出、履歴、入力、ジャンプ処理は既存のまま。

HUD CanvasはPlayerと同じSceneへ所属し、Player破棄時に削除する。タイトル復帰/Player無効化でNoneとなり、旧案内と二重表示しない。

## 検証（2026-10-04）

追加指示のE行最上段/対象あり時だけ表示は、更新済みHUD回帰7/7成功・Skip0（09:09:40Z）。Play中に表示順、対象なし非表示、対象あり表示、対象解除非表示を確認。

正規Unity RunnerでHUD追加7件と既存Interaction/WalkMode/Catalog31件、計38/38成功・Skip0。PlayでPlayer破棄後のCanvas破棄も確認。最終Console Error0。実MainScene PlayでタイトルNone、通常入力Conversation、散歩Walk、Pause None/Resume Walk、対象解除時の基本説明復帰、タイトル復帰None、旧中央Prompt非生成を確認。636×358 Game Viewで全ラベルの枠内表示と描画を確認。Scene/Prefab設定変更・保存は不要。Windows/WebGLの実Buildと非16:9実描画の比較は未実施。既存テストFixtureのBootstrap OnValidate/SendMessage Warningは今回のHUDと別に存在。

## Scene終了時の参照解放（2026-10-05）

SceneアンロードやPlayer破棄では、HUD Canvasが先に破棄される場合がある。RefreshRowsはPanel消失時に行を更新せず、個別に破棄済みの行も操作しない。ControlGuideHUDのOnDestroyはCanvas/Panel/Label/行リストを先に解放し、所有Canvasを削除する。外部からViewが破棄された後の再表示では、残存Canvasと古い行リストを解放してから再生成する。PlayerInteractionPromptの通知先もUnityの破棄済み判定を使用し、破棄済みHUDに通知しない。通常のモード切替・E行・入力非遮断は維持する。

検証: 正規Unity RunnerでHUD全11ケース中10成功、再生成テスト1件の装飾子要素を含む件数の期待値を修正後、単独1/1成功（Skip0）。Canvas先行破棄/HUD先行破棄/Sceneアンロード/再生成/通常モード・案内表示を確認。最終Console Error0、Play停止、Scene dirty=false。Windows再Buildと実Player終了操作は未検証。

## 二重表示・文脈別案内の修正（2026-10-05）

MainSceneへ保存されていた古いControlGuideCanvasを削除。可変行のRuntime CanvasにはDontSaveInEditorを設定してSceneへ再保存されることを防ぐ。Interactorの有効状態・CanInteract・入力状態を確認し、対象なし／Pause／選択肢中／会話モードではE案内を残さない。ウィンドウ内の操作説明は置かず、右下HUDに集約する。「クリック/タップでも操作」は表示しないが、既存マウス・タッチ操作は維持する。

追加修正検証: 関連56/56＋通常選択肢表示条件1/1＋インタラクト可否1/1、合計58ケース成功（失敗0/Skip0）。HD/FHD/WQHD/4K/4:3/21:9で配置と文脈別ラベル収容を確認し、HD/4:3の合成描画を画像確認。実Player/実機操作とWindows再Buildは未実施。

## Editor残存Canvasの根本対策（2026-10-05）

再調査時、Scene rootには存在しないDontSaveInEditorのControlGuideCanvasが8個あり、対応するControlGuideHUDは0個だった。EditModeテストの非ExecuteAlwaysコンポーネントではOnDestroyが実行されない場合があり、Playerとは別rootのCanvasが管理者の破棄後も残存していた。Scene保存防止とroot検索だけでは、この所有者のないEditorオブジェクトを検出・解放できない。

ControlGuideHUDをExecuteAlwaysとしてEditorでの無効化・破棄にも寿命管理を適用する。UIは明示的な有効ControllerのSetModeでのみ生成し、通常Editor更新ではNoneにする。Canvas/Panel/行/ラベル/Modeは所有状態として管理するが、DontSave参照は通常のシリアライズでnullになり得る。OnDisableでViewを非表示・解放し、OnEnableと次の表示要求で安全に再構成するため、参照の保持に寿命を依存させない。OnEnableのResources全体検査で、識別可能なDontSave HUDのうち所有参照のないものだけを非表示・破棄する。他Controllerが所有するViewは維持する。破棄時には先に非表示にし、Play中の遅延Destroyでも古い案内を描画しない。

既存テストへEditMode所有者破棄の6回反復、所有Viewを保持した孤児回収、所有状態のSerialization往復、入力欄のユーザー基準を追加。TearDownはScene外を含むResourcesから全Guide Canvasの所有関係を検証し、テスト成功後に案内が残る状態を失敗として検出する。

根本対策の最終検証: 関連76/76と共通輪郭変更の結果UI11/11、計87ケース成功・失敗0/Skip0。各HUDテスト終了時の孤児検査成功、Resources全体のテスト後Canvas0を確認。MainScene Play内の検証用Controllerで実再コンパイル前1個→旧View消失→再表示1個→Play停止後0個を確認。HD/FHD/WQHD/4K/4:3/21:9の非交差・文字収容検証とHD/4:3の合成描画を確認。輪郭はUIStyle.EnsureBorderでignoreLayoutとして寸法・間隔への加算を防ぐ。入力欄のBaseline照合一致、Scene保存後dirty=false、Console Error0。Windows再Build/実Player物理操作は未実施。


開発環境限定の共通ConsoleはF1で開閉する（Issue #74/#75）。通常Start/Debug Startのゲーム開始後に利用できる。表示中はゲームのEsc/F10/散歩/会話入力を遮断し、閉じたフレームまで誤入力を抑止する。IME変換中の開閉は保留。配布版のゲーム機能でF1を必須にしない。詳細docs/DebugMode.md。
