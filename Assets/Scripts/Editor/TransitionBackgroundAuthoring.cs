#if UNITY_EDITOR
using System;
using Nekolpos.ActionSystem;
using Nekolpos.System;
using Nekolpos.TimeSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.TransitionAuthoring
{
    public static class TransitionBackgroundAuthoring
    {
        public const string PrefabPath = "Assets/Prefab/TransitionBackground.prefab";
        private const string SourcePath = "Assets/Picture/Transition_base.png";
        public const string SunPath = "Assets/Picture/Transition_sun.png";

        public static Sprite EnsureSunSprite(bool regenerate = false)
        {
            if (regenerate || !global::System.IO.File.Exists(SunPath))
            {
                const int size = 256;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color[size * size];
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float radius = new Vector2((x + .5f) / size - .5f, (y + .5f) / size - .5f).magnitude;
                        float body = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.32f, .37f, radius));
                        body *= Mathf.Lerp(1f, .84f, Mathf.SmoothStep(0f, 1f, radius / .35f));
                        float halo = .4f * Mathf.Exp(-Mathf.Pow(radius / .45f, 4f));
                        halo *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, .5f, radius));
                        float alpha = 1f - (1f - body) * (1f - halo);
                        // A brighter centre and warm rim stay pastel under the existing period tint.
                        Color light = Color.Lerp(Color.white, new Color(1f, .94f, .82f), Mathf.SmoothStep(0f, 1f, radius / .35f));
                        light.a = alpha;
                        pixels[y * size + x] = light;
                    }
                    texture.SetPixels(pixels);
                    texture.Apply();
                    global::System.IO.File.WriteAllBytes(SunPath, texture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
                AssetDatabase.ImportAsset(SunPath);
            }
            var importer = AssetImporter.GetAtPath(SunPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Transition sun texture is missing.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SunPath);
        }

        [MenuItem("Nekolpos/UI/Install Transition Background")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before authoring the transition.");
            var fade = UnityEngine.Object.FindFirstObjectByType<FadeController>(FindObjectsInactive.Include);
            var result = UnityEngine.Object.FindFirstObjectByType<ResultTextUI>(FindObjectsInactive.Include);
            if (fade == null || result == null) throw new InvalidOperationException("Open MainScene with its existing fade and result UI.");
            var resultData = new SerializedObject(result);
            Canvas canvas = resultData.FindProperty("rootCanvas").objectReferenceValue as Canvas;
            if (canvas == null) throw new InvalidOperationException("Result UI Canvas is missing.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = CreatePrefab();
            var view = UnityEngine.Object.FindFirstObjectByType<TransitionBackgroundView>(FindObjectsInactive.Include);
            GameObject root;
            if (view == null)
            {
                root = PrefabUtility.InstantiatePrefab(prefab, canvas.transform) as GameObject;
                view = root.GetComponentInChildren<TransitionBackgroundView>(true);
                Undo.RegisterCreatedObjectUndo(root, "Install Transition Background");
            }
            else root = view.transform.parent.gameObject;
            var viewData = new SerializedObject(view);
            viewData.FindProperty("clock").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            viewData.ApplyModifiedPropertiesWithoutUndo();
            var fadeData = new SerializedObject(fade);
            fadeData.FindProperty("rootCanvas").objectReferenceValue = canvas;
            fadeData.FindProperty("fadeGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            fadeData.FindProperty("transitionBackground").objectReferenceValue = view;
            fadeData.FindProperty("transitionMask").objectReferenceValue = root.GetComponent<RectMask2D>();
            fadeData.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);
            EditorUtility.SetDirty(fade);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static GameObject CreatePrefab()
        {
            var importer = AssetImporter.GetAtPath(SourcePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Transition_base.png is missing.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<Sprite>(SourcePath);
            var root = Rect("TransitionBackground", null);
            root.sizeDelta = new Vector2(1280f, 720f);
            try
            {
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                root.gameObject.AddComponent<RectMask2D>();
                Image black = Rect("BlackFallback", root).gameObject.AddComponent<Image>();
                black.color = Color.black;
                var composition = Rect("Composition", root);
                var view = composition.gameObject.AddComponent<TransitionBackgroundView>();
                var sky = Rect("Sky", composition).gameObject.AddComponent<TransitionSkyGraphic>();
                var sunRect = Rect("Sun", composition);
                sunRect.anchorMin = sunRect.anchorMax = new Vector2(.12f, .23f);
                sunRect.sizeDelta = new Vector2(110f, 110f);
                sunRect.anchoredPosition = Vector2.zero;
                var sun = sunRect.gameObject.AddComponent<Image>();
                sun.sprite = EnsureSunSprite();
                sun.raycastTarget = false;
                var frame = Rect("ForegroundFrame", composition);
                var aspect = frame.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                aspect.aspectRatio = 16f / 9f;
                var foreground = Rect("Foreground", frame).gameObject.AddComponent<Image>();
                foreground.sprite = source;
                foreground.raycastTarget = false;
                var data = new SerializedObject(view);
                data.FindProperty("sky").objectReferenceValue = sky;
                data.FindProperty("sun").objectReferenceValue = sun;
                data.FindProperty("foregroundFrame").objectReferenceValue = frame;
                data.FindProperty("foreground").objectReferenceValue = foreground;
                var styles = data.FindProperty("periods");
                styles.arraySize = 4;
                string[] tops = { "#95CDE8", "#69B9ED", "#A77EC8", "#111B44" };
                string[] bottoms = { "#FFF0CB", "#CFF4FF", "#FFB678", "#45517D" };
                string[] suns = { "#FFF0A8", "#FFF9D0", "#FFAD68", "#FFAD68" };
                Vector2[] positions = { new Vector2(.12f, .23f), new Vector2(.32f, .78f), new Vector2(.75f, .88f), new Vector2(.88f, .42f) };
                for (int i = 0; i < 4; i++)
                {
                    var item = styles.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("period").enumValueIndex = i;
                    ColorUtility.TryParseHtmlString(tops[i], out Color top);
                    ColorUtility.TryParseHtmlString(bottoms[i], out Color bottom);
                    ColorUtility.TryParseHtmlString(suns[i], out Color sunColor);
                    if (i == 3) sunColor.a = 0f;
                    item.FindPropertyRelative("top").colorValue = top;
                    item.FindPropertyRelative("bottom").colorValue = bottom;
                    item.FindPropertyRelative("sunColor").colorValue = sunColor;
                    item.FindPropertyRelative("sunPosition").vector2Value = positions[i];
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                view.Synchronize();
                root.gameObject.SetActive(false);
                root.sizeDelta = Vector2.zero;
                return PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }
    }
}
#endif
