using TMPro;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// 【フォントのプリロード】
    /// Dynamicフォントは未使用文字が表示された瞬間にSDF生成とAtlas書き込みが走るため、
    /// 一瞬フレームが止まる（カクつく）問題があります。
    /// これを防ぐため、ゲーム開始時に使用頻度の高い文字を一括生成します。
    /// </summary>
    public class FontPreloader : MonoBehaviour
    {
        [Header("Dynamic TMP Fonts (Assign in Inspector)")]
        public TMP_FontAsset[] targetFonts;

        [Header("Preload Configuration")]
        [TextArea(5, 10)]
        public string preloadCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz" +
            "0123456789" +
            "ぁあぃいぅうぇえぉお" +
            "かきくけこさしすせそ" +
            "たちつてとなにぬねの" +
            "はひふへほまみむめも" +
            "やゆよらりるれろわをん" +
            "アイウエオカキクケコ" +
            "サシスセソタチツテト" +
            "ナニヌネノハヒフヘホ" +
            "マミムメモヤユヨラリルレロワヲン" +
            "！？。、・ー「」『』（）" +
            "猫又主人背中巨大日月火水木金土" +
            "人僕俺君彼彼女何名前今日明昨日朝昼夕夜" +
            "春夏秋冬上下左右前後思出変言話聞見食";

        [Header("Player Input Verification")]
        [TextArea(2, 4)]
        public string playerInputVerificationCharacters =
            "今日、天気、対馬山猫、猫又、お腹、御飯、可愛い、鬱陶しい、髙橋、る";

        private void Awake()
        {
            PreloadFonts();
        }

        private void PreloadFonts()
        {
            if (targetFonts == null || targetFonts.Length == 0) return;

            foreach (var font in targetFonts)
            {
                if (font == null) continue;

                // 文字を追加（内部的にテクスチャアトラスへの焼き込みが行われる）
                font.TryAddCharacters(preloadCharacters + playerInputVerificationCharacters);
            }

            PlayerInputFontAssetProvider.AddCharacters(preloadCharacters + playerInputVerificationCharacters);

            Debug.Log("[FontPreloader] TMP Dynamic Font Preload Completed. (対象フォントに基本文字セットをキャッシュしました)");
        }

        /// <summary>
        /// プレイヤーの自由入力など、新しく登場した文字列のフォントテクスチャを即時生成します
        /// </summary>
        public void AddCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            PlayerInputFontAssetProvider.AddCharacters(text);

            if (targetFonts == null) return;
            
            foreach (var font in targetFonts)
            {
                if (font == null) continue;
                font.TryAddCharacters(text);
            }
        }
    }
}
