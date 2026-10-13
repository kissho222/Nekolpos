# 時間帯連動トランジション背景（Issue #67）

## 素材と描画

正本はユーザー作成の `Assets/Picture/Transition_base.png`（1920×1080 RGBA）。PNGは変更しない。Sprite Single/FullRect、最大2048、MipMapなし、非圧縮でImportし、足跡・淡黄色・斜め境界を画像のまま描画する。

`Assets/Prefab/TransitionBackground.prefab`をMainSceneの既存OpenBetaCanvasへ配置し、OpenBetaSystemsの既存FadeControllerへ接続する。階層はTransitionBackground（CanvasGroup/RectMask2D）→BlackFallback→Composition（画面全体）→Sky→Sun→ForegroundFrame（16:9 EnvelopeParent）→Foreground。表示順は空・太陽・元PNG。その前に既存ActionResultPanelの半透明パネル・TMP本文が表示される。運搬は独立Canvas sortingOrder2000なのでさらに前に表示される。既存ロゴは新設しない。

`TransitionSkyGraphic.cs`は上下2色の頂点カラーを持つuGUI Graphic。標準UI Shaderを使用するため新しいShader/Material/DOTween依存は不要で、RectMask2DやWebGLの標準UI描画経路を使う。空は常にAlpha1、前景の透過部分から3Dが漏れない。BlackFallbackは通常の死亡等の既存黒フェードと、不足時の遮蔽を維持する。

EnvelopeParentで前景だけの比率を維持し画面全体をカバーする。16:9以外では外側の足跡等をクロップする。元画像を伸ばさず、縦画面でも隙間を作らない。空の上下色と太陽は画面基準なので画像のクロップに巻き込まれない。KeepVisibleSunAboveForegroundは可視時間帯の太陽を空側へ収める設定で、縦長では朝のY位置を前景境界＋半径の上まで補正する。BoundaryStartY=0.03/BoundarySlope=0.94は提供画像の斜め境界に対応する位置計算の目安で、境界を描き直すものではない。夜の位置や沈む途中には補正しない。無効化すればInspector指定位置をそのまま使う。

## Inspector設定

Composition上の `TransitionBackgroundView.cs` は既存TimeManagerのCurrentPeriod/OnTimeAdvancedを正とする。PeriodsにMorning/Afternoon/Evening/Nightの4設定を保持する。Top/Bottom/SunColor/SunPositionを調整できる。暫定色はIssue指定どおり。

| 時間帯 | 上端 | 下端 | 太陽の初期位置（左下0,0〜右上1,1） |
|---|---|---|---|
| 朝 | #95CDE8 | #FFF0CB | 0.12, 0.23 |
| 昼 | #69B9ED | #CFF4FF | 0.32, 0.78 |
| 夕 | #A77EC8 | #FFB678 | 0.75, 0.88 |
| 夜 | #111B44 | #45517D | 0.88, 0.42（Alpha0） |

太陽は専用Transition_sun.pngのSpriteを使用する。中心の明るい色から暖色の縁へ緩やかに変化し、外側の淡い光がAlpha0へ溶ける。黒い輪郭・放射線・ギザギザはなく、通常のUI Imageだけで描画する。Lighting/Bloomには依存しない。本体と光は1枚のSpriteなので、前景背面の既存描画順で同時に隠れる。Size/時間帯の色はInspectorで変更可能。夜の位置は斜め前景の下で、前景の不透明領域に隠れつつAlpha0へ補間する。TransitionSeconds（初期1秒）、ArcHeight（初期0.12）で色と弧の補間を調整する。弧は位置LerpにSinの上方向オフセットを加える。夜→朝は不可視の太陽を朝のX位置の画面下へ移し、透明度とY位置を補間して朝の指定位置まで上昇させる。発光も前景背面から現れる。日付跨ぎで前日の太陽がまだ見えている場合は、前半で消してから後半で同じ上昇を行う。太陽は空を横断しない。

文字・背景・余白は既存 `ActionResultPanel.prefab` のImage/TMP/VerticalLayoutGroupから調整する。既存の文言・Variant選択・表示時間・日記記録を再利用し、新規テキストや台詞は生成しない。詳細は `docs/TimedEventResultUI.md`。

## 既存フローとの統合

FadeControllerのBeginTimeTransitionを既存DialogueManagerのRequestTimeAdvanceAsync/翌朝の就寝処理で呼ぶ。従来のFadeOut完了後に時間更新・結果表示を実行し、WaitForTransitionBackgroundAsyncで補間完了を待ってFadeInする。結果本文の表示時間中は並行補間、結果表示後に時間を進めるフローでは補間だけを追加で待つ。完全遮蔽中の位置/視線/翌朝再配置の順序は変更しない。

場所移動のWipeToBlackはBeginLocationTransition/BeginLocationで現在の見た目を取得し、開放完了まで固定する。PlayerAreaTransitionControllerの通常エリア移動やBootstrapの場所Timeline Signalも同じ背景の固定表示を使い、既存Alphaフェード時間を保つ。時間帯変更イベントが途中で来ても背景・太陽を動かさず、非表示後に現在の時間へ同期する。移動に伴う時間変更がなければ見た目は変わらない。WipeFromBlackも左→右を維持する。ワイプはRectMask2D.paddingだけを動かし、構図・太陽・PNGは画面サイズのままクリップする。WipeBoundaryは進行0〜1を確認する値。背景未接続の従来Sceneは元のAnchorワイプへフォールバックする。

演出外の時間変更は即時同期する。非表示中は表示開始時にCurrentPeriodを取得する。完了・即時透明化・無効化・タイトル復帰で補間を終了し、クリップ範囲を復元する。

Editor専用 `TransitionBackgroundAuthoring.cs` の `Nekolpos > UI > Install Transition Background` が初回Prefab作成・既存Canvas/Fade参照を接続する。再実行でPrefabのInspector設定を再生成しない。Sceneはユーザーが保存する。固定UIはRuntimeで生成しない。

## 確認

TransitionBackgroundTestsで4色/夜非表示、演出外同期、場所移動中の固定、時間補間/弧、日付跨ぎの不可視再配置、非正入力/停止、原Sprite参照を確認する。TransportAnimationTestsはワイプの方向/完全被覆イベント/中断復元を維持し、関連TimeFlow/睡眠/結果パネルのテストも実行する。

MainSceneで朝→昼→夕→夜と翌朝、Table⇄Desk、時間経過テキスト、キャンセル・タイトル復帰を確認する。Build・ブラウザー・画面比率別の実測結果はHANDOFF/Issue報告を正とし、未検証を成功扱いにしない。

2026-10-10検証：関連Runner89件成功・Skip0、MainScene実Playで4時間帯/翌朝/両方向運搬/実時間進行、4:3・21:9・9:16の表示を確認。Windows64 ReleaseとWebGL DevelopmentのBuildはError0/Warning0で成功。WebGLはローカルブラウザーで背景・太陽の表示、朝→昼の時間進行とゲーム復帰、机への移動を確認した。起動時に既存URP/TAAの「Trying to load color backbuffer into a complex RenderPass setup」エラー1件があり、描画設定は今回変更していない。既存SoapBubble Shaderフォールバック等の警告も証跡へ記録。モバイル実機・全操作・太陽演出の最終判断は未確認。UnityのBuildTargetはWindows64、GameViewは元の16:9へ復元。証跡はoutput/transition-background。

2026-10-10太陽改善：専用Spriteへ差し替え、Editor生成処理を更新。Runtimeの移動経路・色補間・遷移処理は変更なし。最終関連テスト21/21成功、MainScene実Playで朝・昼・夕の見た目と発光を含む前景遮蔽を確認、Console Error0。今回のSprite差し替え後のPlayer Buildは未実施。証跡output/transition-background/sun。

2026-10-10朝の日の出追加：関連テスト23/23成功。MainScene実Playでは自動Updateを一時停止し、AdvancePresentationを0.15秒ずつ進めた8段階の描画と座標を保存して、前景に隠れた開始位置からの上昇を確認。日/時間帯/Component enabledを復元、Editor停止。今回のPlayer Buildは未実施。証跡output/transition-background/dawn。

死亡専用の黒保持・日の出と前景/空の同期復帰は docs/DeathRevivalTransition.md（Issue #68）を参照。通常Time/Locationには死亡専用暗色を持ち込まない。
