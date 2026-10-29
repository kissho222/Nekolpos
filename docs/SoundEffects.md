# 共通SE再生基盤

Issue #81。既存の `SettingsManager` と `Audio/GameSettings` Mixer（#38）、音量プレビュー（#61）を再利用する。音素材が未登録でも無音で動作し、新しい常駐マネージャーを追加しない。

## 設定と接続

Unityメニュー `ネコルポス > 音声 > SE設定` から対象を選択する。初めて導入するSceneでは不足部品追加を明示実行し、結果を確認して保存する。操作は既存プレイヤー・通常猫・ちゃぶ台/机の床Colliderを一意に確認し、足りない部品だけ追加する。対象が曖昧なら変更せず停止する。

既存のAudioSource、床材、クリップ、空配列、未指定欄を上書きしない。追加する床材は素材未確定・クリップ未登録の無音Profileである。見た目から木・布等の材質を決めない。Scene保存、Play開始、全床への一括設定は自動実行しない。

Undoで取り消すのは追加コンポーネントとその新規参照である。新規の独立Profile Assetは残す。処理中の失敗ではその呼出しで作った空Profileだけを確認して取り消し、既存Assetを削除しない。inactiveな親の既存床指定も保持する。通常猫はbootstrapのnormalCatRoot参照を優先し、オープニングの猫と混同しない。

このPRは大きなMainScene差分を含めない。既存ローカルSceneの接続確認と、baseから新規導入する際のEditor操作を区別する。導入操作を実行していないbase Sceneを接続済みとは扱わない。

## 床材と足音

`FootstepSurfaceProfile` は床名、複数AudioClip、音量倍率を持つ。木・畳・カーペット・布団・タイル・金属・土・その他などを必要に応じて増やせる。`FootstepSurface` をColliderまたは親へ付け、Profileを指定する。

- 明示的に指定したProfileが空またはNull候補のみなら無音。既定の音へ置き換えない。
- 床指定が無い、またはProfile自体が未指定の場合だけキャラクターの既定Profileへ戻る。既定も無ければ無音。
- 複数の有効候補があるときは直前と同じクリップを避ける。1種類だけならその音を使用する。

`CharacterMovementAudioController` は実移動速度・接地Collider・接地状態を受け取る。プレイヤーでは既存の接地SphereCastとCharacterControllerの接地結果を使用し、移動前後の水平変位から速度を得る。入力があっても壁や端で移動していなければ鳴らさない。安全復帰のTeleportを歩行距離に数えない。

自動間隔かAnimation Eventの一方を選ぶ。Animation Eventモードでは公開 `PlayFootstep()` を使い、自動間隔ではこの入口を無視する。どちらも移動中・接地中・非Pause・有効中に限定し、最小間隔を守る。音量、歩行間隔、音程の揺らぎをInspectorで調整できる。

## ジャンプ・着地と停止

成立した上向きジャンプで `NotifyJumpStarted()` を1回通知する。初速0なら再生しない。着地は空中から接地へ変わり、最短滞空時間を満たした時に1回だけ鳴らす。ジャンプ/着地クリップは独立し、未登録は無音。

移動操作の一時停止は `SuspendMovementObservation()` で足音要求だけ止め、Pauseをまたぐ滞空履歴を保つ。Teleportや移動ControllerのDisableは `ResetMovementState()` で過去の接地・滞空・直前クリップを破棄する。音声Controller自体のDisableは専用音源をStopし、状態をresetする。無効中の公開再生入口は何も鳴らさない。

`Time.timeScale <= 0` で専用音源をPauseし、解除時は自分がPauseした音源だけUnPauseする。BGM・ゴロゴロ・既存プレビューの音源へ干渉しない。

## 猫ボイス

`CatVoiceAudioController` に「呼ぶ」「怒り」「嬉しい」「不満」の独立した候補配列と音量を設定する。`PlayCatVoice(CatVoiceType)`、または引数なしの `PlayCall / PlayAngry / PlayHappy / PlayDispleased` をイベントから呼ぶ。空カテゴリは無音。不正なカテゴリも要求を拒否する。

既定では再生中の新要求を無視し、Inspectorで差し替え方式にも切り替えられる。通常会話全件や心理値変更へ自動接続せず、台詞も生成しない。足音/ジャンプ/着地はSE、猫ボイスはCatVoiceグループを通る。Masterは共通の上位グループである。

Inspector下部の4ボタンはUnity標準Editor Previewを使う。これはRuntimeの基本/カテゴリ/Mixer音量の検証には使えない。Preview APIが利用できない環境では警告を示す。

## 日本語Inspector

`SoundEffectsInspectors.cs` の共通描画は、既存のInspectorName/Tooltip属性を正本として通常フィールド・猫声カテゴリ内部にも日本語GUIContentを渡す。Movement、床材Profile、床の指定、および既存の猫ボイスEditorへ適用する。InspectorName単独は[Unityの公式APIではenum値向け](https://docs.unity3d.com/ja/6000.0/ScriptReference/InspectorNameAttribute.html)であるため、通常フィールドに属性があるだけでは日本語表示済みと扱わない。

配列は「候補数」「候補 1」等で編集できる。複数選択では配列サイズ変更と猫声試聴を無効にし、共通要素の混在値は標準のSerializedProperty編集を使う。数値範囲、enumの日本語名、Undo、Prefab overrideを標準PropertyFieldへ委ね、既存の値を描画だけで書き換えない。

## Timelineと猫の歩行

`Assets/Resources/Audio/Signals` の4つのSignalAssetを使用できる。猫ボイスのAwakeは、既存の反応が無いSignalだけをSignalReceiverへ追加する。既存Timelineに発火時刻を追加せず、既存の反応も置換しない。

猫の通常歩行の四足接地・姿勢確定後通知は未接続である。汎用移動Controllerの接続口を用意するが、存在しない歩行イベントを仮定しない。既存 `CatFootstepAudioController`、FootContact、Tablet、ホッケー、撫でる音はこのPRの変更対象外。

## 検証範囲

現行ソースを直接コンパイルする外部検証は `.codex/tools/movement_audio_checks/` を参照する。Unity・音源・Mixerの代役を使い、実通信や通常保存を使わない。選択・発火・停止・カテゴリ分離の振る舞いを検証し、実聴感や物理エンジン全体の成功とは区別する。

今回の実測結果、限定Unity確認、baseへの切出しと未検証範囲は `SoundEffectsVerification-20261030.md` に記録する。正式NUnit/workerを無条件に実行しない。

ユーザーが判断するのは音素材の採否、床の実材質、音量/音程/タイミング、猫の四足校正と演出である。既存の登録素材は維持する。Runtime Mixerの聴感、実端末とBuild全体の受入は個別に確認する。
