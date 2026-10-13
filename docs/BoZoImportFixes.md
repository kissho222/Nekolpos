# BoZo導入時の修正

## 写真撮影用の利用

現在の配置は`Assets/DL Assets/BoZo_StylizedModularCharacters`。下記修正時のパスからフォルダ移動済み。

1. 現在の撮影Sceneの作業を保存してから、素材の`Scenes/StylizedCharacterCreator.unity`を開いてPlayし、専用画面で身体・髪・衣服を選ぶ。
2. 下着は`Prefabs/Common/Resources/UnderUpper`のSimpleBra／SimpleUnderShirt、`UnderLower`のSimplePanties／SimpleBoxers。シンプルな服は`Prefabs/Base/Resources/Top/BSMC_Top_Shirt`、`Bottom/BSMC_Bottom_Shorts`等。
3. 固定した外見を撮影へ持ち込む場合は、キャラクターのOutfitSystem InspectorのAdvanced OptionsでPrefab Nameを指定し、Merge Character完了後にSave as Prefab。保存するとstaticになり、その個体の着せ替えはできなくなるため、下着用と普段着用は別名で保存する。
4. 保存先はTools > BoZo Tools > SettingsのPrefab Folderで設定する。生成Prefabを撮影Sceneへ配置する。Play終了前にPrefabを書き出す。専用画面のキャラクターデータ保存とは別操作。

上記は同梱Scene・Prefab・Inspector実装の確認に基づく手順。Creatorの実Play操作・Prefab出力は未実行。写真用の最小構成として使い、ゲーム内着せ替えシステムの追加は不要。

## 2026-10-03の対応

Unity 6000.3.10f1、`Assets/BoZo_StylizedModularCharacters`が対象。Openなcodex-task Issueなし。購入素材のRuntime構成、ゲームScene、台詞は変更しない。

### 原因と変更

- `Outfits/Common/BSMC_Head/BSMC_Head.fbx`は実骨が`pelvis`／`spine_01`系なのに、Humanoid定義と保存Skeletonが旧`Hips`／`Spine`系だった。`BodyRig.fbx`の検証済みHuman対応表と、対象モデル自身のTransformから作ったSkeletonへ更新。既存Avatarの名前・モデルGUIDを維持して正常なHumanoid Avatarを再生成。
- 頭Avatarをコピーする衣服18件も保存Human/Skeletonが古いため、同じ対応表と各モデル自身のSkeletonへ更新。さらにJeans、CoolShoes、Shirt、TankTopの4件はコピー元参照が欠落していたため、修復済みの頭Avatarを指定。衣服22件はHumanoid／CopyFromOtherを維持。
- `Animations/BMAC_HeelHeight.fbx`はBodyRigのSkeletonコピーと実モデルの足先位置に約1～2mm差があり、骨長不一致になっていた。Human対応表は共有し、Skeletonは対象自身から生成するCreateFromThisModelへ変更。Clip名・既存参照用ID・フレーム範囲は維持。
- `OutfitSystem.cs`と`CharacterCreator/CreatorSaveLoad.cs`のイベント入口でLoad/MergeのTaskをawaitし、処理の完了・例外を観測。`ExpressionSelect.cs`、`ThirdParty/MagicaCloth/BoZo_MagicaClothSupport.cs`から実際には非同期処理のないasync指定を除去。`BoZoCharacterOptimizer.cs`の未使用privateフィールドを除去。警告抑制指定は追加していない。
- `Demo/Materials/BSMC_Skybox.mat`の内部名をファイル名へ合わせた。

### 確認結果

- Unityでスクリプト再コンパイル成功。BoZo由来のC#警告なし。
- 全モデル117件をForceUpdateで再インポート。Console Error 0、Hips欠落／コピー元骨長不一致の再発なし。
- Humanoidモデル25件の全Human骨名が実Transformへ解決。独自生成Avatar3件はisValid/isHumanともtrue、CopyFromOther22件のコピー元も同条件を満たす。
- スキン付きRenderer142件のMeshと全bones参照に欠落なし。Avatar使用Prefab3件のAvatarも正常。
- HeelHeight_1/_2は空でないHuman Motionとして読込。
- Play Modeでの着せ替え・ロード・ヒール姿勢、実Player Buildは未確認。現在のMainPhotoSceneの未保存状態を保持し、Scene保存・切替・Play操作なし。
- リポジトリ全体の`git diff --check`は既存変更の`Assets/Editor/TextEditPreview/TextEditPreviewState.asset:32`末尾空白で失敗。このファイルは今回変更していない。

### 残る警告と確認事項

- `HeelHeight_0`は素材設定がstart=0/end=0で、Unityではempty=true。BlendTreeのHeelHeight=0とHeelTestから参照されているため、削除や別フレームへの拡張はしない。ニュートラル姿勢の意図を見た目で確認したうえで、必要なら正しい固定姿勢Clipへ置換する。
- Body_BasicBody、BSMC_Head_02、Head_AnimeYoung、Head_V2はFBXにsmoothing groupsがなく、一部BlendShapeの法線生成が失敗する。法線計算方式の一律変更は外見を変えるため未実施。素材側の再出力か、代表的な表情・体形での比較を伴うImporter調整が必要。
- PlayerLookTarget/WalkDialogueControllerの今回検出したC#警告は2026-10-04に修正済み。VS/Unity messagingのUDP 56626競合や素材法線警告は再発時に個別調査する。
- 購入素材更新で今回の修正が上書きされる可能性がある。再導入後は上記確認を再実施する。

修正前FBXメタデータ117件と変更一覧24件は`C:/Users/shoho/.codex/tmp/bozo-import-20261003`へ退避。購入素材フォルダは現時点でGit未追跡。新規C#ファイルは追加していない。


### 2026-10-04 Console警告修正

OutfitSystem.SaveCharacterToPrefabはEditorでasync/awaitを維持し、Playerでは従来の何もしない動作をTask.CompletedTaskで返す。Playerのawaitなしasync警告を解消した。BMAC_Toonは追加ライトmulti_compileの重複を除去し、導入済みURPの_CLUSTER_LIGHT_LOOP/USE_CLUSTER_LIGHT_LOOPへ更新。位置.xyz、UV Scale.xy、Shadow Color.rgb、scalar flat/fogCoordを明示して、暗黙に捨てられていた成分を既存どおり扱う。BoZo_LayerTextureShaderもScale.xyとscalar flatへ修正。材質設定やSceneは変更しない。

Editor再コンパイル、Windows非Developmentスクリプト36アセンブリのコンパイル成功。ShaderVariantCollectionで通常/追加ライト+衣装+輪郭/Cluster追加ライトの代表variantをWarmUpし、両Shaderの診断0、Console Warning/Error0を確認。散歩会話の既存回帰2件成功。全Shader variant・見た目比較・全Player Buildは未検証。購入素材更新で修正が上書きされる点は従来どおり。
