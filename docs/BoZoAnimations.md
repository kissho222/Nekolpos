# BoZoアニメーション一覧

2026-10-04、導入済み`Assets/DL Assets/BoZo_StylizedModularCharacters`をUnity AssetDatabaseから確認。プレビュー用を除くAnimationClipは23件。日本語は名前に基づく訳で、F/Mは女性用/男性用として表記。Scene・Animator・Clip設定は変更していない。

| AnimationClip名 | 日本語訳 | 所在フォルダ |
|---|---|---|
| BMAC_Dizzy | めまい・ふらつき | Animations |
| BMAC_F_Idle | 女性用・待機 | Animations |
| BMAC_F_Walk | 女性用・歩行 | Animations |
| BMAC_M_Idle | 男性用・待機 | Animations |
| BMAC_M_Idle02 | 男性用・待機その2 | Animations |
| BMAC_M_Walk | 男性用・歩行 | Animations |
| BSMC_T-Pose | Tポーズ | Outfits/Common/BSMC_Head |
| Blink | 瞬き | Animations/Expressions |
| BSMC_Blink | 瞬き（別パターン） | Animations/Expressions |
| BSMC_Angry | 怒った表情 | Animations/Expressions |
| BSMC_Devious | いたずらっぽい・企んだ表情 | Animations/Expressions |
| BSMC_Evil | 邪悪な表情 | Animations/Expressions |
| BSMC_Happy | 嬉しい表情 | Animations/Expressions |
| BSMC_Neutral | 通常・無表情 | Animations/Expressions |
| BSMC_Sad | 悲しい表情 | Animations/Expressions |
| BSMC_Smile | 微笑み | Animations/Expressions |
| BSMC_Surprised | 驚いた表情 | Animations/Expressions |
| BSMC_HeeledFeet | ヒールを履いた足の形 | Animations/Expressions |
| HeelHeight_0 | ヒール高さ0 | Animations/BMAC_HeelHeight.fbx内 |
| HeelHeight_1 | ヒール高さ1 | Animations/BMAC_HeelHeight.fbx内 |
| HeelHeight_2 | ヒール高さ2 | Animations/BMAC_HeelHeight.fbx内 |
| BSMC_Demo_Idle | デモ用・待機 | Demo/Animations |
| BMSC_Demo_IdleFaceAnim | デモ用・待機中の顔アニメーション | Demo/Animations |

`BSMC_Neutral`と`HeelHeight_0`は現在empty=true、Curve Binding数0。表情Clipにはlength=0でもCurveを持つ固定表情があり、長さ0だけで空Clipとは判定しない。`BMSC_Demo_IdleFaceAnim`の接頭辞は実ファイルどおりBMSC。
