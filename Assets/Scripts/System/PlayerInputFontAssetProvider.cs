using TMPro;
using UnityEngine;

namespace Nekolpos.System
{
    public static class PlayerInputFontAssetProvider
    {
        public const string PlayerInputFontResourcePath = "Fonts/PlayerInputZenMaruGothicDynamic SDF";
        public const string PlayerInputFallbackFontResourcePath = "Fonts/PlayerInputNotoSansJPDynamicFallback SDF";

        private static TMP_FontAsset playerInputFont;
        private static TMP_FontAsset fallbackFont;

        public static TMP_FontAsset PlayerInputFont => playerInputFont ??= Resources.Load<TMP_FontAsset>(PlayerInputFontResourcePath);

        public static TMP_FontAsset FallbackFont => fallbackFont ??= Resources.Load<TMP_FontAsset>(PlayerInputFallbackFontResourcePath);

        public static bool TryGetFallbackFont(out TMP_FontAsset font)
        {
            font = FallbackFont;
            return IsUsableFontAsset(font);
        }

        public static void ApplyToPlayerInput(TMP_InputField inputField)
        {
            if (inputField == null)
            {
                return;
            }

            ApplyToPlayerInput(inputField.textComponent);
        }

        public static void ApplyToPlayerInput(TMP_Text text)
        {
            TMP_FontAsset font = PlayerInputFont;
            if (text == null || !IsUsableFontAsset(font))
            {
                return;
            }

            try
            {
                text.font = font;
                if (font.material != null)
                {
                    text.fontSharedMaterial = font.material;
                }

                text.SetAllDirty();
            }
            catch (global::System.Exception exception)
            {
                Debug.LogWarning($"[PlayerInputFontAssetProvider] Failed to apply player input font: {exception.Message}");
            }
        }

        public static void AddCharacters(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            TMP_FontAsset font = PlayerInputFont;
            if (IsUsableFontAsset(font))
            {
                TryAddCharacters(font, text);
            }

            TMP_FontAsset fallback = FallbackFont;
            if (IsUsableFontAsset(fallback))
            {
                TryAddCharacters(fallback, text);
            }
        }

        private static bool IsUsableFontAsset(TMP_FontAsset font)
        {
            if (font == null)
            {
                return false;
            }

            try
            {
                Texture2D[] atlasTextures = font.atlasTextures;
                if (atlasTextures == null || atlasTextures.Length == 0 || atlasTextures[0] == null)
                {
                    Debug.LogWarning($"[PlayerInputFontAssetProvider] TMP font asset '{font.name}' has no atlas texture. Skipping player input font override.");
                    return false;
                }
            }
            catch (global::System.Exception exception)
            {
                Debug.LogWarning($"[PlayerInputFontAssetProvider] TMP font asset '{font.name}' is not usable: {exception.Message}");
                return false;
            }

            return true;
        }

        private static void TryAddCharacters(TMP_FontAsset font, string text)
        {
            try
            {
                font.TryAddCharacters(text);
            }
            catch (global::System.Exception exception)
            {
                Debug.LogWarning($"[PlayerInputFontAssetProvider] Failed to add characters to '{font.name}': {exception.Message}");
            }
        }
    }
}
