# MainPhotoScene 撮影スタジオ

`Assets/Scenes/MainPhotoScene.unity` を単独で開いてPlayする。`PhotoStudioController` はこのScene名でのみ起動する。通常のMainScene／GameSceneへ追加しない。

## 操作

- 「視線ON」で既存LookRigがPhotoCameraへ追従。「角度保持OFF」は頭・首・両目の現在のローカル角度を保持。「正面へ」は起動時のAnimatorニュートラルへ戻して固定する。固定中はHead／EyeのYaw・Pitchを調整可能。
- 「首のX軸（上下）の可動域制限を無視」「首のY軸（左右）の可動域制限を無視」は個別に切替可能。PhotoStudio Inspectorの`Ignore Head X Limit`／`Ignore Head Y Limit`も同じ設定。既存LookRigの上限値は書き換えず、MainPhotoScene内の追従時だけ制限を解除する。
- 「UIを隠す」とF1は、Scene内Canvasと操作UI・構図ガイドをまとめて切り替える。非表示中もF1で復帰可能。
- 位置・角度・FOVは数値入力。右ドラッグでカメラ回転、WASD／QEで移動、Shiftで高速、Altで低速。ゲーム本編の入力仕様には影響しない。入力欄編集中は移動キーを抑止する。
- カメラ名を入力してSave Camera Preset。複数保存でき、同名は更新。各名前ボタンで復元、Reset Cameraは起動時へ戻す。Copy ValuesでJSONをコピー。
- ガイドは三分割・中央・90% Safe Area。16:9ボタンと任意の幅／高さ比入力を使用する。ガイドはPNGに含まれない。
- Pause Pose／Resume PoseはAnimator速度と再生中Timelineを保持して停止・復帰。1 Frameは1/60秒進める。既存Animator StateボタンとTimelineの時刻スライダーで撮影姿勢を選ぶ。
- Morning／Day／Evening／NightとSunny／Cloudy／Rainyは既存RoomLightingManagerへ直接指定し、TimeManagerへは通知しない。
- 撮影ControllerのInspectorとPlayメニューのスライダーでDoF・Focus Distance・Aperture・Focal Length・Bloom・Exposureを調整。Exposure上書きをOFFにすれば時間帯の露出を使用する。
- InspectorからPostを調整する場合は`PhotoStudio > PhotoStudioController`の`Dof Enabled / Focus Distance / Aperture / Focal Length`等を使う。UIと共通の値で、共有Profile assetは撮影中の操作対象にしない。Focus Distanceはm、Focal Lengthはmm。ピント距離が焦点距離以下になるとBokehが成立しないため安全な下限へ補正する。例: DoF ON、Focus Distance 0.5、Aperture 1、Focal Length 100でぼけを確認できる。
- **DoFはGameビューで確認する。** 使用中URPのPostProcessPassはSceneビューでDoFを実行しない。VolumeのDepthOfFieldへ直接書いた値は、撮影Controllerが毎LateUpdateに書き戻す。Inspectorの「描画中のDoF」で、Volumeを合成した実際のMode・Focus・Aperture・Focal Lengthを確認できる。「頭にピントを合わせる」はカメラ前方方向の頭までの距離を使用。被写体の大きさはカメラFOVで調整する。
- Focus Distanceは0.1〜20m、Apertureは1〜32、Focal Lengthは1〜300mm、Bloomは0〜5、Exposureは-5〜5EVのスライダー。Apertureを下げる／Focal Lengthを上げるとピント面の外側がぼけやすい。DoF/BloomのONとExposure上書きのONはそれぞれ別チェックボックス。
- Cat Position／RotationとReset Catで被写体Rootを調整。変更はPlay中だけ。
- PhotoCameraや猫RootのTransformはInspectorで直接編集可能。カメラの移動目標と猫Rootの撮影姿勢へ取り込むため元へ戻らない。「前の部位／次の部位」で骨を選び、Local Position／RotationをUI編集できる。骨TransformのInspector編集も取り込み、自動でポーズ停止・視線保持に切り替える。視線ONは頭首目の手動固定を解除、Animator State選択は部位の手動調整を解除する。
- Editorでの部位調整は`PhotoStudio`を選び「部位をEditorで調整」で部位名を検索。部位一覧から選んでLocal Position／RotationとX/Y/Zスライダーを使用する。「Hierarchyで部位を選択」後は通常のTransform InspectorやSceneビューの回転ハンドルでも編集できる。Play中はUndo/Redo通知を撮影姿勢キャッシュへ即時反映するため、Ctrl+Z／Ctrl+Y後にLateUpdateが旧角度へ戻さない。Play停止時には変更が戻る。Edit Modeの変更も可能だが、Play開始時のAnimator Rebindで初期姿勢へ戻るので、今回の主用途はPlay中のEditor操作。
- Capture PNGで1920×1080、UIなしのPNGを出力。日時＋カメラ名のファイル名と保存先をConsole／操作UIに表示。`Capture(width,height)` APIは64〜8192pxに対応する。

## 保存と分離

カメラプリセットは`Application.persistentDataPath/PhotoStudio/camera-presets.json`、PNGは`PhotoStudio/Captures`。本編のPlayerPrefsやセーブファイルを使用しない。不正なプリセットファイルは警告を出して読み込まない。

PhotoScene内のゲーム進行・会話・タブレット・Player・Navigation・API送信Rootは、Awakeが走らないようSceneデータで非アクティブ化する。通常猫のゲーム用制御とNavMeshAgentも停止。既存Animator／LookRig、部屋・照明は再利用する。Timelineのゲーム進行Signal bindingは撮影起動時に解除する。

Volume Profileは実行時に複製する。撮影用RoomLightingManagerの`photoStudioPreview`は時間帯Profileの本番asset作成／保存を抑止する。既存の通常Sceneでは既定falseのまま。

## 構成・再設定

- `PhotoStudioController.cs`: 撮影UI、視線保持、ポーズ、プリセット、PNG。
- `PhotoStudioControllerEditor.cs`（Editor専用）: Postスライダー・描画中のDoF表示、検索付き部位編集、TransformのUndo/Redoと撮影姿勢キャッシュの同期。本編Scene・BuildにEditorフックを持ち込まない。
- `PhotoShootCameraController.cs`: 既存Free Cameraを再利用。プリセット復元時には`SetPose`で補間内部位置も同期。
- `PhotoStudioSceneSetup.cs`（Editor専用）: `Nekolpos > Photo Studio > Configure MainPhotoScene`。開いているPhotoSceneだけを設定・保存する。別Sceneを保存しない。
- `Assets/Settings/PhotoStudioPost.asset`: 撮影専用の追加Volume初期値。

モデル差し替え時はPhotoStudioのSubject、Animator、LookRig、Head／Neck／Eyes参照を更新する。撮影Sceneの構成変更後は必要に応じて再設定する。猫の台詞やゲーム進行演出は追加しない。

2026-10-03の二尾OB反映は既存Normal_Catの骨格・Renderer・制御参照を維持し、表示Meshだけを`Assets/Prefab/Neruko_TwoTails_OB_UnityMesh.asset`へ変更した。新FBXの1/100単位を補正した派生Meshであり、FBX更新時は再生成が必要。通常Animatorの13クリップ65姿勢からBoundsを更新。MainPhotoSceneで待機/あくび/歩行、尾の骨操作と実材質による下面表示を確認後、MainSceneの通常用/演出用の猫へ反映済み。PhotoStudioのSubject/Animator/LookRig/頭・首・目参照は同じObjectを保持している。詳細は`docs/NerukoTwoTailsOB.md`。

## 検証（2026-10-03）

DoF/Editor操作の追加対応: MainPhotoSceneの撮影Volume priority=100、通常Volume=0、照明Volume=20。本編NekolposDepthOfFieldControllerは非Activeで、DoFへ書き込まない。Volumeへの直接Focus=7入力は次フレームにPhotoStudioControllerの0.5へ戻ることを実証。URP実装`Library/PackageCache/com.unity.render-pipelines.universal@37e0d4fc2503/Runtime/Passes/PostProcessPass.cs`でもSceneViewのDoF除外を確認。撮影CameraのPost Processing・Depth Texture・Volume Layer・EveryFrame更新をControllerで確保する。

Test Runner EditModeでPhotoStudioTests全5件成功（完了5、失敗0）。追加はUndo/Redo後の保持と対象外Transform拒否。既存PostテストへCamera設定の復旧検証も追加。テスト用姿勢はPreviewSceneで作り、元Sceneを保存しない。

実MainPhotoScene Playで頭の角度変更・Undo・Redoを複数フレーム後に確認して角度差0。通常Transform編集経由のleg_f.Lも変更・Undo・Redo差0。部位50件、専用Inspectorの登録を確認。DoFの合成後設定はBokeh/Focus0.5/Aperture1/FocalLength100を維持。960×540同一姿勢ON/OFF PNGのRGB絶対平均差11.80、隣接画素差はOFF5.42→ON0.82でぼけを確認。「頭にピント」のFocus=0.299650mはカメラ前方の頭までの距離と一致。骨内部位置への合焦なので、表面へ合わせるにはスライダーで微調整する。

最終Console Error0、Play停止。MainPhotoSceneは作業前後のSHA256一致・Scene自動保存なし。通常MainSceneや共有PostProfileは編集しない。新規Editor C#はPhotoStudioControllerEditor.csのみ。変更はPhotoStudioController.cs、既存PhotoStudioTests.cs、専用Inspectorとmeta、この仕様書、HANDOFF。Windows/WebGL Player Buildとマウス/物理Ctrl+Zキー入力の検証は未実施（Undo/Redo API・実Transform経由で検証済み）。補助ファイルは`C:/Users/shoho/.codex/tmp/photo-editor-20261003`。

Editor直接実行の回帰2件成功（Test Runner実行ではない）。Profileの深い複製、別Controllerへのプリセット再読込、同名更新、不正FOV・空名の拒否を確認。

実PhotoScene Playで頭・首・両目の保持後角度差0度、正面保持、Animator停止中の進行差0、1フレーム送り、Timeline停止・再開と1/60秒送り、UI表示復帰、複数プリセット復元後のカメラ位置安定、Cat Root復元、12照明組合せ、DoF／BloomのOFF上書きを確認。1920×1080 PNGに猫が描画され、UI／構図ガイドが含まれないことを画像で確認。操作UIの日本語表示も確認。

撮影中の有効TimeManagerは0、本番Profile assetのハッシュ変更0。最初のSmoke時点以降のEditor PlayerPrefsに差分0。最新Console Error0。MainSceneのファイル更新はユーザー自身の保存と確認済み。元のMainSceneの有効状態を復元し、未保存変更を残してPlay停止、検証で加算ロードしたPhotoSceneを閉じた。F1の物理キー入力と最終的な構図／視線の見た目は人による確認対象。

追加修正: カメラ／猫Root／頭のInspector編集と前脚のUI用API編集を複数フレーム後に確認し、変更差0。首の制限解除はXのみでPitch45→57.688度（Yaw45維持）、YのみでYaw45→109.682度（Pitch45維持）、両方解除も確認。DoFを同一姿勢・同一フレームでON/OFF撮影する場合にもVolume Stackを更新する。960×540 PNGのRGB平均差約12.12/10.93/10.01とぼけを確認。無効なレンズ設定の補正／Profile非破壊を加え、Editor直接実行回帰3/3成功。PhotoSceneのユーザー未保存状態を維持、Play停止。
