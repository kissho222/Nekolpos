using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Nekolpos.EditorTools
{
    public static class PlayerInputFontAssetBuilder
    {
        private const string ResourceFolder = "Assets/Resources/Fonts";
        private const string PlayerInputAssetPath = ResourceFolder + "/PlayerInputZenMaruGothicDynamic SDF.asset";
        private const string FallbackAssetPath = ResourceFolder + "/PlayerInputNotoSansJPDynamicFallback SDF.asset";
        private const string ZenSourceFontPath = "Assets/Fonts/ZenMaruGothic-Regular.ttf";
        private const string FallbackSourceFontPath = "Assets/Fonts/NotoSansJP-Regular.ttf";

        [MenuItem("Nekolpos/Fonts/Ensure Player Input TMP Font Assets")]
        public static void EnsurePlayerInputFontAssets()
        {
            Directory.CreateDirectory(ResourceFolder);

            TMP_FontAsset fallback = EnsureDynamicFontAsset(
                FallbackAssetPath,
                FallbackSourceFontPath,
                "PlayerInputNotoSansJPDynamicFallback SDF",
                null);

            TMP_FontAsset playerInput = EnsureDynamicFontAsset(
                PlayerInputAssetPath,
                ZenSourceFontPath,
                "PlayerInputZenMaruGothicDynamic SDF",
                fallback);

            string verificationText = "今日、天気、対馬山猫、猫又、お腹、御飯、可愛い、鬱陶しい、髙橋、る";
            playerInput.TryAddCharacters(verificationText);
            fallback.TryAddCharacters(verificationText);

            EditorUtility.SetDirty(playerInput);
            EditorUtility.SetDirty(fallback);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[PlayerInputFontAssetBuilder] Ensured player input TMP font assets. " +
                $"PlayerInput={PlayerInputAssetPath}, Fallback={FallbackAssetPath}");
        }

        private static TMP_FontAsset EnsureDynamicFontAsset(
            string assetPath,
            string sourceFontPath,
            string assetName,
            TMP_FontAsset fallback)
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fontAsset != null && !HasUsableAtlasTexture(fontAsset))
            {
                AssetDatabase.DeleteAsset(assetPath);
                fontAsset = null;
            }

            if (fontAsset == null)
            {
                Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
                if (sourceFont == null)
                {
                    throw new FileNotFoundException($"Source font was not found: {sourceFontPath}");
                }

                fontAsset = TMP_FontAsset.CreateFontAsset(
                    sourceFont,
                    90,
                    10,
                    GlyphRenderMode.SDFAA,
                    2048,
                    2048,
                    AtlasPopulationMode.Dynamic,
                    true);
                fontAsset.name = assetName;
                AssetDatabase.CreateAsset(fontAsset, assetPath);
                AddFontSubAssets(fontAsset);
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.fallbackFontAssetTable = fallback != null
                ? new List<TMP_FontAsset> { fallback }
                : new List<TMP_FontAsset>();

            EditorUtility.SetDirty(fontAsset);
            return fontAsset;
        }

        private static bool HasUsableAtlasTexture(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return false;
            }

            Texture2D[] atlasTextures = fontAsset.atlasTextures;
            return atlasTextures != null && atlasTextures.Length > 0 && atlasTextures[0] != null && fontAsset.material != null;
        }

        private static void AddFontSubAssets(TMP_FontAsset fontAsset)
        {
            if (fontAsset.material != null)
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            Texture2D[] atlasTextures = fontAsset.atlasTextures;
            if (atlasTextures == null)
            {
                return;
            }

            for (int i = 0; i < atlasTextures.Length; i++)
            {
                if (atlasTextures[i] != null)
                {
                    AssetDatabase.AddObjectToAsset(atlasTextures[i], fontAsset);
                }
            }
        }
    }
}
