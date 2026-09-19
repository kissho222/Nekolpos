using System.IO;
using Nekolpos.System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.EditorTools
{
    public static class TimeOfDayWidgetSceneBuilder
    {
        private const string WidgetName = "TimeOfDayWidget";
        private const string CircleSpritePath = "Assets/Resources/UIThemes/Sprites/time_of_day_circle.png";
        private const string ArrowSpritePath = "Assets/Resources/UIThemes/Sprites/time_of_day_arrow.png";
        private const string AutoBuildRequestPath = "Temp/BuildTimeOfDayWidget.request";

        [InitializeOnLoadMethod]
        private static void BuildFromRequestFile()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(AutoBuildRequestPath))
                {
                    return;
                }

                File.Delete(AutoBuildRequestPath);
                Build();
            };
        }

        public static void Build()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[TimeOfDayWidgetSceneBuilder] Canvas が見つかりません。");
                return;
            }

            Sprite circleSprite = EnsureCircleSprite();
            Sprite arrowSprite = EnsureArrowSprite();

            Transform existing = FindDescendant(canvas.transform, WidgetName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject root = CreateRect(WidgetName, canvas.transform, new Vector2(150f, 178f));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = new Vector2(-24f, -24f);

            TimeOfDayWidget widget = root.AddComponent<TimeOfDayWidget>();
            TimeOfDayWidgetBinder binder = root.AddComponent<TimeOfDayWidgetBinder>();
            root.AddComponent<CanvasGroup>();

            TextMeshProUGUI dayText = CreateText("DayText", root.transform, "1日目", 22f, FontStyles.Bold);
            RectTransform dayRect = dayText.rectTransform;
            dayRect.anchorMin = new Vector2(0f, 1f);
            dayRect.anchorMax = new Vector2(1f, 1f);
            dayRect.pivot = new Vector2(0.5f, 1f);
            dayRect.anchoredPosition = Vector2.zero;
            dayRect.sizeDelta = new Vector2(0f, 30f);

            GameObject circleRoot = CreateRect("CircleRoot", root.transform, new Vector2(128f, 128f));
            RectTransform circleRect = circleRoot.GetComponent<RectTransform>();
            circleRect.anchorMin = new Vector2(0.5f, 0f);
            circleRect.anchorMax = new Vector2(0.5f, 0f);
            circleRect.pivot = new Vector2(0.5f, 0f);
            circleRect.anchoredPosition = new Vector2(0f, 0f);

            Image sectorMorning = CreateSector("SectorMorning", circleRoot.transform, circleSprite, 45f);
            Image sectorNoon = CreateSector("SectorNoon", circleRoot.transform, circleSprite, -45f);
            Image sectorEvening = CreateSector("SectorEvening", circleRoot.transform, circleSprite, 225f);
            Image sectorNight = CreateSector("SectorNight", circleRoot.transform, circleSprite, 135f);

            Image arrow = CreateImage("Arrow", circleRoot.transform, arrowSprite, new Vector2(34f, 52f));
            arrow.raycastTarget = false;
            arrow.rectTransform.anchoredPosition = Vector2.zero;

            TextMeshProUGUI labelMorning = CreateLabel("LabelMorning", circleRoot.transform, "朝", new Vector2(0f, 48f));
            TextMeshProUGUI labelNoon = CreateLabel("LabelNoon", circleRoot.transform, "昼", new Vector2(48f, 0f));
            TextMeshProUGUI labelEvening = CreateLabel("LabelEvening", circleRoot.transform, "夕", new Vector2(0f, -48f));
            TextMeshProUGUI labelNight = CreateLabel("LabelNight", circleRoot.transform, "夜", new Vector2(-48f, 0f));

            SetWidgetReferences(
                widget,
                dayText,
                labelMorning,
                labelNoon,
                labelEvening,
                labelNight,
                sectorMorning,
                sectorNoon,
                sectorEvening,
                sectorNight,
                arrow.rectTransform);

            SetBinderReferences(binder, widget);
            widget.SetState(1, TimeOfDay.Morning);

            root.transform.SetAsLastSibling();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[TimeOfDayWidgetSceneBuilder] TimeOfDayWidget を作成しました。");
        }

        private static GameObject CreateRect(string name, Transform parent, Vector2 size)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(obj, $"Create {name}");
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return obj;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            GameObject obj = CreateRect(name, parent, size);
            Image image = obj.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateSector(string name, Transform parent, Sprite sprite, float rotationZ)
        {
            Image image = CreateImage(name, parent, sprite, new Vector2(128f, 128f));
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0.25f;
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
            return image;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, FontStyles style)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(80f, 24f));
            TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.94f, 0.70f, 1f);
            label.raycastTarget = false;
            return label;
        }

        private static TextMeshProUGUI CreateLabel(string name, Transform parent, string text, Vector2 position)
        {
            TextMeshProUGUI label = CreateText(name, parent, text, 18f, FontStyles.Normal);
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = new Vector2(32f, 24f);
            return label;
        }

        private static void SetWidgetReferences(
            TimeOfDayWidget widget,
            TMP_Text dayText,
            TMP_Text labelMorning,
            TMP_Text labelNoon,
            TMP_Text labelEvening,
            TMP_Text labelNight,
            Image sectorMorning,
            Image sectorNoon,
            Image sectorEvening,
            Image sectorNight,
            RectTransform arrowRect)
        {
            SerializedObject serialized = new SerializedObject(widget);
            serialized.FindProperty("dayText").objectReferenceValue = dayText;
            serialized.FindProperty("labelMorning").objectReferenceValue = labelMorning;
            serialized.FindProperty("labelNoon").objectReferenceValue = labelNoon;
            serialized.FindProperty("labelEvening").objectReferenceValue = labelEvening;
            serialized.FindProperty("labelNight").objectReferenceValue = labelNight;
            serialized.FindProperty("sectorMorning").objectReferenceValue = sectorMorning;
            serialized.FindProperty("sectorNoon").objectReferenceValue = sectorNoon;
            serialized.FindProperty("sectorEvening").objectReferenceValue = sectorEvening;
            serialized.FindProperty("sectorNight").objectReferenceValue = sectorNight;
            serialized.FindProperty("arrowRectTransform").objectReferenceValue = arrowRect;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBinderReferences(TimeOfDayWidgetBinder binder, TimeOfDayWidget widget)
        {
            SerializedObject serialized = new SerializedObject(binder);
            serialized.FindProperty("widget").objectReferenceValue = widget;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite EnsureCircleSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
            if (existing != null)
            {
                return existing;
            }

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = (size - 2) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    texture.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), center) <= radius ? Color.white : Color.clear);
                }
            }

            return SaveSpriteTexture(texture, CircleSpritePath);
        }

        private static Sprite EnsureArrowSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
            if (existing != null)
            {
                return existing;
            }

            const int width = 64;
            const int height = 96;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, Color.clear);
                }
            }

            Vector2 tip = new Vector2(width * 0.5f, height - 6f);
            Vector2 left = new Vector2(8f, height * 0.45f);
            Vector2 right = new Vector2(width - 8f, height * 0.45f);
            Rect stem = new Rect(width * 0.39f, 10f, width * 0.22f, height * 0.48f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    if (PointInTriangle(p, tip, left, right) || stem.Contains(p))
                    {
                        texture.SetPixel(x, y, Color.white);
                    }
                }
            }

            return SaveSpriteTexture(texture, ArrowSpritePath);
        }

        private static Sprite SaveSpriteTexture(Texture2D texture, string path)
        {
            texture.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float area = 0.5f * (-b.y * c.x + a.y * (-b.x + c.x) + a.x * (b.y - c.y) + b.x * c.y);
            float s = 1f / (2f * area) * (a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y);
            float t = 1f / (2f * area) * (a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y);
            return s >= 0f && t >= 0f && (1f - s - t) >= 0f;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindDescendant(root.GetChild(i), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
