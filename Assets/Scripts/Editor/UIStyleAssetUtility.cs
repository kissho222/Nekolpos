using System.IO;
using Nekolpos.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Nekolpos.EditorTools
{
    public static class UIStyleAssetUtility
    {
        private const string ThemeFolder = "Assets/Resources/UIThemes";
        private const string SpriteFolder = "Assets/Resources/UIThemes/Sprites";
        private const string NormalProfilePath = ThemeFolder + "/NekolposNormalUITheme.asset";
        private const string UneasyProfilePath = ThemeFolder + "/NekolposUneasyUITheme.asset";

        public static void CreateDefaultThemeAssets()
        {
            EnsureFolders();
            Sprite panel = CreateRoundedSpriteAsset(SpriteFolder + "/ui_panel_round.png", 18, 8);
            Sprite button = CreateRoundedSpriteAsset(SpriteFolder + "/ui_button_round.png", 13, 6);
            Sprite input = CreateRoundedSpriteAsset(SpriteFolder + "/ui_input_round.png", 11, 6);
            Sprite border = CreateRoundedSpriteAsset(SpriteFolder + "/ui_border_round.png", 18, 8);

            UIThemeProfile normal = LoadOrCreateProfile(NormalProfilePath);
            normal.theme = UIThemeKind.Normal;
            normal.panelSprite = panel;
            normal.buttonSprite = button;
            normal.inputSprite = input;
            normal.borderSprite = border;
            normal.panelColor = UIStyle.NormalPanel;
            normal.buttonColor = UIStyle.NormalButton;
            normal.inputColor = UIStyle.NormalInput;
            normal.borderColor = UIStyle.NormalBorder;
            normal.shadowColor = UIStyle.NormalShadow;
            normal.textColor = UIStyle.NormalText;
            normal.placeholderTextColor = UIStyle.NormalPlaceholder;
            normal.buttonHighlightedColor = UIStyle.NormalHighlighted;
            normal.buttonPressedColor = UIStyle.NormalPressed;
            normal.buttonDisabledColor = UIStyle.NormalDisabled;
            EditorUtility.SetDirty(normal);

            UIThemeProfile uneasy = LoadOrCreateProfile(UneasyProfilePath);
            uneasy.theme = UIThemeKind.Uneasy;
            uneasy.panelSprite = panel;
            uneasy.buttonSprite = button;
            uneasy.inputSprite = input;
            uneasy.borderSprite = border;
            uneasy.panelColor = UIStyle.UneasyPanel;
            uneasy.buttonColor = UIStyle.UneasyButton;
            uneasy.inputColor = UIStyle.UneasyInput;
            uneasy.borderColor = UIStyle.UneasyBorder;
            uneasy.shadowColor = UIStyle.UneasyShadow;
            uneasy.textColor = UIStyle.UneasyText;
            uneasy.placeholderTextColor = new Color(0.78f, 0.77f, 0.72f, 0.48f);
            uneasy.buttonHighlightedColor = new Color(0.31f, 0.33f, 0.36f, 0.98f);
            uneasy.buttonPressedColor = new Color(0.16f, 0.17f, 0.19f, 0.98f);
            uneasy.buttonDisabledColor = new Color(0.18f, 0.18f, 0.19f, 0.45f);
            EditorUtility.SetDirty(uneasy);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UIStyleAssetUtility] Nekolpos UI theme assets created.");
        }

        public static void ApplyRoundedStyleToOpenScenesAndPrefabs()
        {
            CreateDefaultThemeAssets();
            UIThemeProfile profile = AssetDatabase.LoadAssetAtPath<UIThemeProfile>(NormalProfilePath);
            ApplyToOpenScenes(profile);
            ApplyToPrefabAssets(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[UIStyleAssetUtility] Rounded UI style applied to open scenes and prefab assets. Backups are under ProjectBackups/UIStyle.");
        }

        private static void ApplyToOpenScenes(UIThemeProfile profile)
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Canvas[] canvases = roots[i].GetComponentsInChildren<Canvas>(true);
                    for (int j = 0; j < canvases.Length; j++)
                    {
                        UIStyle.ApplyTree(canvases[j].transform, profile);
                        UIThemeApplier applier = canvases[j].GetComponent<UIThemeApplier>();
                        if (applier == null)
                        {
                            applier = canvases[j].gameObject.AddComponent<UIThemeApplier>();
                        }

                        applier.Profile = profile;
                        EditorUtility.SetDirty(canvases[j]);
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void ApplyToPrefabAssets(UIThemeProfile profile)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponentInChildren<Graphic>(true) == null)
                {
                    continue;
                }

                BackupAsset(path);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                UIStyle.ApplyTree(root.transform, profile);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static UIThemeProfile LoadOrCreateProfile(string path)
        {
            UIThemeProfile profile = AssetDatabase.LoadAssetAtPath<UIThemeProfile>(path);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<UIThemeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!AssetDatabase.IsValidFolder(ThemeFolder))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "UIThemes");
            }

            if (!AssetDatabase.IsValidFolder(SpriteFolder))
            {
                AssetDatabase.CreateFolder(ThemeFolder, "Sprites");
            }
        }

        private static Sprite CreateRoundedSpriteAsset(string path, int radius, int border)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32 clear = new Color32(255, 255, 255, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            float r = Mathf.Clamp(radius, 1f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r), 0f);
                    float dy = Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r), 0f);
                    texture.SetPixel(x, y, dx * dx + dy * dy <= r * r ? white : clear);
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BackupAsset(string assetPath)
        {
            string backupRoot = "ProjectBackups/UIStyle";
            Directory.CreateDirectory(backupRoot);
            string timestamp = global::System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = Path.GetFileName(assetPath);
            string backupPath = Path.Combine(backupRoot, timestamp + "_" + fileName);
            File.Copy(assetPath, backupPath, true);

            string metaPath = assetPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Copy(metaPath, backupPath + ".meta", true);
            }
        }
    }
}
