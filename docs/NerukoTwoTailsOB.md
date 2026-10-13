# ネルコ二尾 OB 仮モデル

更新: 2026-10-03

## ファイルと保護

- 作業ファイル: `D:/Blender/Neruko_TwoTails_OB.blend`
- 下面修正前の退避: `D:/Blender/Neruko_TwoTails_OB_beforeUndersideFix.blend`
- 元ファイル: `D:/Blender/尻尾増殖手術.blend`。SHA256一致で未変更を確認。元FBXへの書き込み・Exportは実行していない。

## 現在の構造

`Arm_Cat` と `Cat_1` の2 Object。50ボーンと `tail_03 → tail_04 → tail_05` の構造を維持。二尾は `Cat_1` の連続したY字メッシュであり、追加専用Object/Boneはない。

既存先端形状を複製して横へ開き、尻尾の接合部分だけをBoolean Unionで接合後、本体側の既存リングへ溶接した。全身へのBooleanは採用していない。下面の板状部分をなくすため、尻尾の独立した毛用メッシュ135頂点と追加側の毛用メッシュ53頂点を除去。尻尾以外の毛用メッシュは維持。

接合部は元/追加側の近傍4頂点から既存ボーンウェイトを距離の逆二乗で補間・正規化。元座標と一致する頂点は元ウェイトを使用。UVMapとCat材質を継承。最終メッシュ6250頂点/5837面。修正はOB仮モデル内に限定され、元モデルのディスク上データには適用していない。

## 確認

- 下・斜め下・上の静止画像、下からのPose画像を確認。板状面・接合の継ぎ目が解消され、二股を視認できる。
- 尻尾先端領域の開いた辺0、非多様体辺0、未ウェイト頂点0。
- 尻尾以外の5937頂点を修正前の作業メッシュと照合。座標差0、ウェイト差0。
- Armature移動への追従誤差最大4.01e-8。
- tail_03/04/05を10/25/-20度変更して両尾が追従。異常な非有限座標なし。確認後にポーズを復元して保存。
- Blenderを下面が見えるViewへ変更。

確認画像: `D:/Blender/Neruko_TwoTails_OB_underside.png`、`underside_rear.png`、`underside_pose.png`（後二者も同じNeruko_TwoTails_OB_接頭辞）。

## Unity向け出力

Arm_CatとCat_1を一緒に選択して、新規名のFBXへ出力する。既存Armature Modifierはスキニング用に保持し、手動で適用して静的メッシュにしない。Add Leaf BonesはOFF。以前のNeruko_TailBranch_OBは接合後に削除済みなので出力対象ではない。

Animationデータは現在のblendに含まれていない。Blender材質のAlbedo/Normalは元の旧Eドライブ参照で画像size0のため、Blender側の画像はWorkbenchによる形状確認。

## Unity Sceneへの反映（2026-10-03）

ユーザーが取り込んだ `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Neruko_TwoTails_OB.fbx` を使用。まずMainPhotoSceneのNormal_Catで検証してから、MainSceneのNormal_CatとOP_Cat Bへ反映した。MainPhotoSceneの非ActiveなOP_Cat Bは今回の置換対象外。

元のGameObject、Renderer、50本の骨、Animator Controller、Avatar、LookRig、Timeline Binding、移動・会話・撮影参照を保持し、SkinnedMeshRendererのsharedMeshとlocalBoundsだけを変更。共有Cat_Collar_C1 Prefabや元FBX/旧モデルは編集していない。

新FBXのメッシュ頂点/Bind Pose並進が既存モデルの1/100で、FBXのRenderer側Scaleが100だった。そのため新FBXを直接既存Rendererへ割り当てず、頂点を100倍し、骨名を照合して既存Bind Poseへ合わせた派生Mesh `Assets/Prefab/Neruko_TwoTails_OB_UnityMesh.asset` を作成。7213頂点、50 Bind Poses。正規化後のBind Pose比較差最大6.79e-6。FBXを更新した場合、この派生Meshは自動更新されないため再生成が必要。

Normal Animatorの13クリップ65姿勢、OP Animatorの99クリップ495姿勢をPreviewSceneでサンプルし、各姿勢前にTransformを初期化。描画Boundsを計算して0.04m拡張した。NormalのBounds中心(0.0384,0.2164,-0.0541)/Extents(0.2589,0.2560,0.4405)。OPは位置を含む演出を覆うため中心(0,0.3028,2.4651)/Extents(1.4607,1.0001,3.3741)。広めのOP Boundsは画面端で消えることを避けるための暫定設定。

実PlayのMainPhotoSceneで待機・あくび・歩行、PhotoStudioのtail_04操作（変形差最大0.0580m）を確認。CatSim_color_6による下面画像で二股と接合面を確認。MainSceneの両猫で尻尾変形（OP 0.0619m/Normal 0.0581m）とRoot移動追従（誤差最大1.13e-7m）、Avatar正常、骨欠落0、通常モードでNormal Renderer ON/OP Renderer OFFを確認。最終Console Error0。MainSceneのMissing Scriptは調査時0、MainPhotoSceneの最終Missing Script0。

保存Sceneを修正前の読み込み状態と照合し、MainPhotoSceneはメッシュ/Bounds Override追加のみ、MainSceneは2体のメッシュ/Bounds変更のみ。検証中のTMP色キャッシュ差は復元。MainSceneの停止後の自動UIレイアウト更新は退避して保存せず閉じた。作業前に存在したMainPhotoSceneの未保存変更は退避した上で置換と一緒に保存。最終的にPlay停止、MainPhotoSceneを開く。

退避Scene・実Playレポート・画像: `C:/Users/shoho/.codex/tmp/neruko-two-tails-replace-20261003`。Player Build、全Timelineの通し再生、スマートフォン実機は未検証。新規C#なし。
