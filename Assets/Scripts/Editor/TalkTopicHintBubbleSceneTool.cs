using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Nekolpos.EditorTools
{
    public static class TalkTopicHintBubbleSceneTool
    {
        private const string BubblePath = "Assets/Resources/UIThemes/Sprites/talk_topic_bubble_circle.png";
        private const string ButtonPath = "OpenBetaCanvas/DialogueUI/TalkTopicHintBubbleButton";
        private const string LabelName = "TalkTopicHintBubbleLabel";
        private const string BorderName = "UIStyleBorder";
        private static readonly Color LabelTextColor = Color.white;
        private static readonly Color32 LabelOutlineColor = new Color32(0, 0, 0, 235);
        private static readonly Color LabelOutlineGraphicColor = new Color(0f, 0f, 0f, 235f / 255f);

        [InitializeOnLoadMethod]
        private static void RegisterAutoPreview()
        {
            EditorApplication.delayCall -= ApplyBubblePreviewIfPresent;
            EditorApplication.delayCall += ApplyBubblePreviewIfPresent;

            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.sceneOpened += HandleSceneOpened;

            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        public static void ApplyBubblePreview()
        {
            if (ApplyBubblePreviewInternal(logWhenMissing: true))
            {
                Debug.Log("[TalkTopicHintBubbleSceneTool] Applied circular bubble preview to TalkTopicHintBubbleButton.");
            }
        }

        private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            EditorApplication.delayCall += ApplyBubblePreviewIfPresent;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += ApplyBubblePreviewIfPresent;
            }
        }

        private static void ApplyBubblePreviewIfPresent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            ApplyBubblePreviewInternal(logWhenMissing: false);
        }

        private static bool ApplyBubblePreviewInternal(bool logWhenMissing)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            Sprite bubbleSprite = EnsureBubbleSprite();
            if (bubbleSprite == null)
            {
                Debug.LogError("[TalkTopicHintBubbleSceneTool] Bubble sprite could not be created.");
                return false;
            }

            GameObject button = FindSceneObjectByPath(ButtonPath);
            if (button == null)
            {
                if (logWhenMissing)
                {
                    Debug.LogWarning($"[TalkTopicHintBubbleSceneTool] '{ButtonPath}' was not found in the active scene.");
                }

                return false;
            }

            bool changed = false;
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            if (buttonRect != null)
            {
                Vector2 targetSize = new Vector2(200f, 200f);
                if (buttonRect.sizeDelta != targetSize)
                {
                    Undo.RecordObject(buttonRect, "Apply Talk Topic Bubble Preview");
                    buttonRect.sizeDelta = targetSize;
                    changed = true;
                }
            }

            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
            {
                Undo.RecordObject(buttonImage, "Apply Talk Topic Bubble Preview");
                changed |= buttonImage.sprite != bubbleSprite;
                changed |= buttonImage.type != Image.Type.Simple;
                changed |= !buttonImage.fillCenter;
                changed |= !buttonImage.preserveAspect;
                changed |= buttonImage.color != Color.white;
                changed |= !buttonImage.raycastTarget;
                buttonImage.sprite = bubbleSprite;
                buttonImage.type = Image.Type.Simple;
                buttonImage.fillCenter = true;
                buttonImage.preserveAspect = true;
                buttonImage.color = Color.white;
                buttonImage.raycastTarget = true;
            }

            Transform border = button.transform.Find(BorderName);
            if (border != null && border.gameObject.activeSelf)
            {
                Undo.RecordObject(border.gameObject, "Apply Talk Topic Bubble Preview");
                border.gameObject.SetActive(false);
                changed = true;
            }

            changed |= ApplyLabelReadability(button.transform);

            if (changed)
            {
                EditorUtility.SetDirty(button);
                EditorSceneManager.MarkSceneDirty(button.scene);
            }

            return true;
        }

        private static bool ApplyLabelReadability(Transform buttonTransform)
        {
            if (buttonTransform == null)
            {
                return false;
            }

            Transform labelTransform = buttonTransform.Find(LabelName);
            TMP_Text label = labelTransform != null
                ? labelTransform.GetComponent<TMP_Text>()
                : buttonTransform.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                return false;
            }

            bool changed = false;
            Undo.RecordObject(label, "Apply Talk Topic Bubble Label Outline");
            if (label.color != LabelTextColor)
            {
                label.color = LabelTextColor;
                changed = true;
            }

            if (!Color32Equals(label.outlineColor, LabelOutlineColor))
            {
                label.outlineColor = LabelOutlineColor;
                changed = true;
            }

            if (!Mathf.Approximately(label.outlineWidth, 0.18f))
            {
                label.outlineWidth = 0.18f;
                changed = true;
            }

            Shadow shadow = label.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = Undo.AddComponent<Shadow>(label.gameObject);
                changed = true;
            }

            Undo.RecordObject(shadow, "Apply Talk Topic Bubble Label Outline");
            Color shadowColor = new Color(0f, 0f, 0f, 0.24f);
            Vector2 shadowDistance = new Vector2(0f, -1.25f);
            if (shadow.effectColor != shadowColor)
            {
                shadow.effectColor = shadowColor;
                changed = true;
            }

            if (shadow.effectDistance != shadowDistance)
            {
                shadow.effectDistance = shadowDistance;
                changed = true;
            }

            if (!shadow.useGraphicAlpha)
            {
                shadow.useGraphicAlpha = true;
                changed = true;
            }

            Outline outline = label.GetComponent<Outline>();
            if (outline == null)
            {
                outline = Undo.AddComponent<Outline>(label.gameObject);
                changed = true;
            }

            Undo.RecordObject(outline, "Apply Talk Topic Bubble Label Outline");
            Vector2 outlineDistance = new Vector2(1.15f, 1.15f);
            if (outline.effectColor != LabelOutlineGraphicColor)
            {
                outline.effectColor = LabelOutlineGraphicColor;
                changed = true;
            }

            if (outline.effectDistance != outlineDistance)
            {
                outline.effectDistance = outlineDistance;
                changed = true;
            }

            if (!outline.useGraphicAlpha)
            {
                outline.useGraphicAlpha = true;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(label);
                EditorUtility.SetDirty(label.gameObject);
            }

            return changed;
        }

        private static bool Color32Equals(Color32 a, Color32 b)
        {
            return a.r == b.r &&
                   a.g == b.g &&
                   a.b == b.b &&
                   a.a == b.a;
        }

        private static GameObject FindSceneObjectByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string[] parts = path.Split('/');
            if (parts.Length == 0)
            {
                return null;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform current = roots[i] != null ? roots[i].transform : null;
                if (current == null || current.name != parts[0])
                {
                    continue;
                }

                for (int partIndex = 1; partIndex < parts.Length && current != null; partIndex++)
                {
                    current = FindDirectChild(current, parts[partIndex]);
                }

                if (current != null)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static Sprite EnsureBubbleSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(BubblePath);
            if (existing != null)
            {
                return existing;
            }

            Texture2D texture = CreateBubbleTexture(256);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string folder = Path.GetDirectoryName(BubblePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllBytes(BubblePath, png);
            AssetDatabase.ImportAsset(BubblePath);

            TextureImporter importer = AssetImporter.GetAtPath(BubblePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(BubblePath);
        }

        private static Texture2D CreateBubbleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "talk_topic_bubble_circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    Vector2 p = uv * 2f - Vector2.one;
                    float radius = p.magnitude;
                    float angle = Mathf.Atan2(p.y, p.x);
                    float inside = 1f - SmoothStep(0.985f, 1.0f, radius);

                    float rim = SmoothStep(0.78f, 0.98f, radius) * (1f - SmoothStep(0.985f, 1.0f, radius));
                    float fineRim = SmoothStep(0.925f, 0.982f, radius) * (1f - SmoothStep(0.986f, 1.0f, radius));
                    float innerFilm = (1f - SmoothStep(0.06f, 0.78f, radius)) * 0.018f;

                    float interference = rim * (0.36f + 0.28f * Mathf.Sin(angle * 2.4f + radius * 7.0f));
                    Color film = Color.Lerp(
                        new Color(0.62f, 0.92f, 1f, 1f),
                        new Color(1f, 0.68f, 0.90f, 1f),
                        Mathf.Sin(angle + 0.7f) * 0.5f + 0.5f);
                    film = Color.Lerp(film, new Color(0.78f, 0.70f, 1f, 1f), Mathf.Sin(angle * 1.7f) * 0.15f + 0.15f);

                    Vector2 crescentP = (uv - new Vector2(0.36f, 0.66f)) * new Vector2(1.0f, 1.65f);
                    Vector2 crescentCut = (uv - new Vector2(0.43f, 0.62f)) * new Vector2(1.0f, 1.65f);
                    float crescent = (1f - SmoothStep(0.18f, 0.30f, crescentP.magnitude))
                                     - (1f - SmoothStep(0.14f, 0.27f, crescentCut.magnitude));
                    crescent = Mathf.Clamp01(crescent) * 0.42f;

                    Vector2 lowerP = (uv - new Vector2(0.66f, 0.34f)) * new Vector2(1.35f, 1.0f);
                    float lower = (1f - SmoothStep(0.18f, 0.34f, lowerP.magnitude)) * 0.12f;

                    Color color = film * Mathf.Clamp01(interference * 0.34f);
                    color += Color.white * (fineRim * 0.50f + crescent);
                    color += new Color(0.68f, 0.92f, 1f, 1f) * (innerFilm + lower);
                    float alpha = inside * Mathf.Clamp01(innerFilm + rim * 0.18f + fineRim * 0.34f + crescent + lower);
                    color.a = alpha;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return texture;
        }

        private static float SmoothStep(float from, float to, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }
    }
}
